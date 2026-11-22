use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::collections::BTreeSet;
use std::io::{BufRead, BufReader, Write};
use std::time::Duration;
use thiserror::Error;

pub const PROTOCOL_VERSION: u32 = 2;
pub const MAX_FRAME_BYTES: usize = 256 * 1024;
pub const PIPE_NAME: &str = "SecRandom_IPC_SecRandom_3F2A1B0E";
pub const EVENT_PIPE_NAME: &str = "SecRandom_IPC_Events_SecRandom_3F2A1B0E";

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DrawChannel {
    RollCall,
    Quick,
    Lottery,
}

impl DrawChannel {
    fn method(self) -> &'static str {
        match self {
            Self::RollCall => "draw.rollCall.trigger",
            Self::Quick => "draw.quick.trigger",
            Self::Lottery => "draw.lottery.trigger",
        }
    }

    fn main_page(self) -> &'static str {
        if matches!(self, Self::Lottery) {
            "main.lottery"
        } else {
            "main.rollCall"
        }
    }
}

#[derive(Debug, Clone, Default, Serialize)]
pub struct DrawOptions {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub list: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub count: Option<u32>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub gender: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub group: Option<String>,
    #[serde(skip)]
    pub open_main_window: bool,
}

impl DrawOptions {
    fn params(&self) -> Option<Value> {
        let params = serde_json::to_value(self).expect("DrawOptions is serializable");
        (!params.as_object().is_some_and(|object| object.is_empty())).then_some(params)
    }
}

#[derive(Debug, Clone, Deserialize)]
pub struct AutomationResponse {
    pub version: u32,
    #[serde(rename = "type")]
    pub frame_type: String,
    pub id: Option<String>,
    pub ok: bool,
    pub result: Option<Value>,
    pub error: Option<AutomationError>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct AutomationError {
    pub code: String,
    pub message: String,
    #[serde(default)]
    pub retryable: bool,
    pub details: Option<Value>,
}

#[derive(Debug, Error)]
pub enum Error {
    #[error("automation transport failed: {0}")]
    Io(#[from] std::io::Error),
    #[error("invalid automation JSON: {0}")]
    Json(#[from] serde_json::Error),
    #[error("automation frame exceeds the protocol limit")]
    FrameTooLarge,
    #[error("{code}: {message}")]
    Remote {
        code: String,
        message: String,
        retryable: bool,
        details: Option<Value>,
    },
}

pub struct AutomationClient {
    pipe_name: String,
    timeout: Duration,
}

impl Default for AutomationClient {
    fn default() -> Self {
        Self::new(PIPE_NAME)
    }
}

impl AutomationClient {
    pub fn new(pipe_name: impl Into<String>) -> Self {
        Self {
            pipe_name: pipe_name.into(),
            timeout: Duration::from_secs(3),
        }
    }

    pub fn with_timeout(mut self, timeout: Duration) -> Self {
        self.timeout = timeout;
        self
    }

    pub fn call(&self, method: &str, params: Option<Value>) -> Result<AutomationResponse, Error> {
        if method.is_empty() {
            return Err(Error::Remote {
                code: "invalid_parameter".into(),
                message: "method is required".into(),
                retryable: false,
                details: None,
            });
        }
        let mut request = json!({
            "version": PROTOCOL_VERSION,
            "type": "rpc",
            "id": format_uuid(),
            "method": method,
        });
        if let Some(params) = params {
            request["params"] = params;
        }
        let line = serde_json::to_vec(&request)?;
        if line.len() + 1 > MAX_FRAME_BYTES {
            return Err(Error::FrameTooLarge);
        }

        let mut stream = connect(&self.pipe_name, self.timeout)?;
        stream.write_all(&line)?;
        stream.write_all(b"\n")?;
        stream.flush()?;

        let mut reader = BufReader::new(stream);
        let mut response_line = String::new();
        reader.read_line(&mut response_line)?;
        if response_line.is_empty() || response_line.as_bytes().len() > MAX_FRAME_BYTES {
            return Err(Error::FrameTooLarge);
        }
        let response: AutomationResponse = serde_json::from_str(&response_line)?;
        if !response.ok {
            let error = response.error.unwrap_or(AutomationError {
                code: "invalid_response".into(),
                message: "SecRandom returned an unsuccessful response without an error".into(),
                retryable: false,
                details: None,
            });
            return Err(Error::Remote {
                code: error.code,
                message: error.message,
                retryable: error.retryable,
                details: error.details,
            });
        }
        Ok(response)
    }

    pub fn draw(&self, channel: DrawChannel, options: DrawOptions) -> Result<Option<Value>, Error> {
        if options.open_main_window {
            self.call(
                "window.show",
                Some(json!({ "target": "main", "page": channel.main_page() })),
            )?;
        }
        Ok(self.call(channel.method(), options.params())?.result)
    }

    pub fn activate_roster(&self, kind: &str, list: &str) -> Result<Option<Value>, Error> {
        if !matches!(kind, "students" | "prizes") || list.is_empty() {
            return Err(Error::Remote {
                code: "invalid_parameter".into(),
                message: "kind must be students or prizes and list is required".into(),
                retryable: false,
                details: None,
            });
        }
        Ok(self
            .call(
                "roster.activate",
                Some(json!({ "kind": kind, "list": list })),
            )?
            .result)
    }

    pub fn roster_lists(&self) -> Result<Option<Value>, Error> {
        Ok(self.call("roster.lists", None)?.result)
    }

    pub fn read_roster(
        &self,
        kind: &str,
        list: Option<&str>,
        include_disabled: bool,
    ) -> Result<Option<Value>, Error> {
        if !matches!(kind, "students" | "prizes") {
            return Err(Error::Remote {
                code: "invalid_parameter".into(),
                message: "kind must be students or prizes".into(),
                retryable: false,
                details: None,
            });
        }
        let mut params = json!({
            "roster_kind": kind,
            "include_disabled": include_disabled,
        });
        if let Some(list) = list {
            params["list_name"] = Value::String(list.to_owned());
        }
        Ok(self.call("roster.read", Some(params))?.result)
    }

    pub fn roster_selection_options(
        &self,
        kind: &str,
        list: Option<&str>,
        include_disabled: bool,
    ) -> Result<Value, Error> {
        let snapshot = self
            .read_roster(kind, list, include_disabled)?
            .unwrap_or_else(|| json!({ "roster_kind": kind, "lists": [] }));
        let mut members = Vec::new();
        let mut groups = BTreeSet::new();
        let mut genders = BTreeSet::new();
        if let Some(lists) = snapshot.get("lists").and_then(Value::as_array) {
            for roster in lists {
                if let Some(items) = roster.get("members").and_then(Value::as_array) {
                    for member in items {
                        if let Some(group) = member.get("group").and_then(Value::as_str) {
                            groups.insert(group.to_owned());
                        }
                        if let Some(gender) = member.get("gender").and_then(Value::as_str) {
                            genders.insert(gender.to_owned());
                        }
                        members.push(member.clone());
                    }
                }
            }
        }
        Ok(json!({
            "roster_kind": snapshot.get("roster_kind").and_then(Value::as_str).unwrap_or(kind),
            "members": members,
            "groups": groups.into_iter().collect::<Vec<_>>(),
            "genders": genders.into_iter().collect::<Vec<_>>(),
        }))
    }

    pub fn show_main_window(&self, page: Option<&str>) -> Result<Option<Value>, Error> {
        let mut params = json!({ "target": "main" });
        if let Some(page) = page {
            params["page"] = Value::String(page.to_owned());
        }
        Ok(self.call("window.show", Some(params))?.result)
    }

    pub fn pipe_name(&self) -> &str {
        &self.pipe_name
    }
}

fn format_uuid() -> String {
    // A unique request id is useful for logs, but the server does not require UUID semantics.
    format!("{:032x}", next_request_id())
}

fn next_request_id() -> u128 {
    use std::sync::atomic::{AtomicU64, Ordering};
    static COUNTER: AtomicU64 = AtomicU64::new(1);
    let now = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos() as u64;
    ((now as u128) << 64) | COUNTER.fetch_add(1, Ordering::Relaxed) as u128
}

#[cfg(unix)]
fn connect(
    pipe_name: &str,
    timeout: Duration,
) -> Result<std::os::unix::net::UnixStream, std::io::Error> {
    let temp_dir = std::env::var_os("TMPDIR").unwrap_or_else(|| "/tmp".into());
    let path = std::path::PathBuf::from(temp_dir).join(format!("CoreFxPipe_{pipe_name}"));
    let stream = std::os::unix::net::UnixStream::connect(path)?;
    stream.set_read_timeout(Some(timeout))?;
    stream.set_write_timeout(Some(timeout))?;
    Ok(stream)
}

#[cfg(windows)]
fn connect(pipe_name: &str, _timeout: Duration) -> Result<std::fs::File, std::io::Error> {
    std::fs::OpenOptions::new()
        .read(true)
        .write(true)
        .open(format!(r"\\.\pipe\{pipe_name}"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn defaults_are_sparse() {
        assert!(DrawOptions::default().params().is_none());
        let options = DrawOptions {
            count: Some(2),
            ..Default::default()
        };
        assert_eq!(options.params().unwrap()["count"], 2);
    }
}
