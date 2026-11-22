from __future__ import annotations

import json
import os
import socket
import time
import uuid
from dataclasses import dataclass
from enum import Enum
from typing import Any, BinaryIO


PROTOCOL_VERSION = 2
MAX_FRAME_BYTES = 256 * 1024
PIPE_NAME = "SecRandom_IPC_SecRandom_3F2A1B0E"
EVENT_PIPE_NAME = "SecRandom_IPC_Events_SecRandom_3F2A1B0E"


class DrawChannel(str, Enum):
    ROLL_CALL = "roll_call"
    QUICK = "quick"
    LOTTERY = "lottery"

    @property
    def method(self) -> str:
        return {
            DrawChannel.ROLL_CALL: "draw.rollCall.trigger",
            DrawChannel.QUICK: "draw.quick.trigger",
            DrawChannel.LOTTERY: "draw.lottery.trigger",
        }[self]

    @property
    def main_page(self) -> str:
        return "main.lottery" if self is DrawChannel.LOTTERY else "main.rollCall"


@dataclass(slots=True)
class DrawOptions:
    """Optional overrides; None means use the setting already selected in SecRandom."""

    list: str | None = None
    count: int | None = None
    gender: str | None = None
    group: str | None = None
    open_main_window: bool = False

    def to_params(self) -> dict[str, Any] | None:
        values = {
            "list": self.list,
            "count": self.count,
            "gender": self.gender,
            "group": self.group,
        }
        return {key: value for key, value in values.items() if value is not None} or None


@dataclass(slots=True)
class AutomationError:
    code: str
    message: str
    retryable: bool = False
    details: Any = None


class AutomationException(RuntimeError):
    def __init__(self, error: AutomationError):
        super().__init__(f"{error.code}: {error.message}")
        self.error = error


class AutomationClient:
    """One-request-per-connection client for the current-user SecRandom pipe."""

    def __init__(self, pipe_name: str = PIPE_NAME, timeout: float = 3.0):
        self.pipe_name = pipe_name
        self.timeout = timeout

    def call(self, method: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
        if not method:
            raise ValueError("method is required")
        request = {
            "version": PROTOCOL_VERSION,
            "type": "rpc",
            "id": uuid.uuid4().hex,
            "method": method,
        }
        if params is not None:
            request["params"] = params
        payload = (json.dumps(request, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")
        if len(payload) > MAX_FRAME_BYTES:
            raise ValueError("automation request exceeds the protocol frame limit")

        with self._connect() as pipe:
            pipe.write(payload)
            pipe.flush()
            line = self._readline(pipe)
        if len(line) > MAX_FRAME_BYTES:
            raise ValueError("automation response exceeds the protocol frame limit")
        response = json.loads(line.decode("utf-8"))
        if not response.get("ok", False):
            error_payload = response.get("error") or {
                "code": "invalid_response",
                "message": "SecRandom returned an unsuccessful response without an error",
            }
            raise AutomationException(AutomationError(
                code=error_payload.get("code", "invalid_response"),
                message=error_payload.get("message", "unknown error"),
                retryable=bool(error_payload.get("retryable", False)),
                details=error_payload.get("details"),
            ))
        return response

    def draw(self, channel: DrawChannel, options: DrawOptions | None = None) -> Any:
        options = options or DrawOptions()
        if options.open_main_window:
            self.call("window.show", {"target": "main", "page": channel.main_page})
        return self.call(channel.method, options.to_params()).get("result")

    def activate_roster(self, kind: str, list_name: str) -> Any:
        if kind not in {"students", "prizes"}:
            raise ValueError("kind must be 'students' or 'prizes'")
        if not list_name:
            raise ValueError("list_name is required")
        return self.call("roster.activate", {"kind": kind, "list": list_name}).get("result")

    def roster_lists(self) -> dict[str, Any]:
        """Return available student lists and prize pools without changing the active choice."""
        return self.call("roster.lists").get("result") or {"students": [], "prizes": []}

    def read_roster(
        self,
        kind: str,
        list_name: str | None = None,
        include_disabled: bool = False,
    ) -> dict[str, Any]:
        """Read members, including gender and group values, for a user-facing selector."""
        if kind not in {"students", "prizes"}:
            raise ValueError("kind must be 'students' or 'prizes'")
        params: dict[str, Any] = {
            "roster_kind": kind,
            "include_disabled": include_disabled,
        }
        if list_name is not None:
            params["list_name"] = list_name
        return self.call("roster.read", params).get("result") or {
            "roster_kind": kind,
            "lists": [],
        }

    def roster_selection_options(
        self,
        kind: str,
        list_name: str | None = None,
        include_disabled: bool = False,
    ) -> dict[str, Any]:
        """Flatten a roster read into members plus distinct group/gender choices."""
        snapshot = self.read_roster(kind, list_name, include_disabled)
        members = [
            member
            for roster in snapshot.get("lists", [])
            for member in roster.get("members", [])
        ]
        groups = sorted({member["group"] for member in members if member.get("group")})
        genders = sorted({member["gender"] for member in members if member.get("gender")})
        return {
            "roster_kind": snapshot.get("roster_kind", kind),
            "members": members,
            "groups": groups,
            "genders": genders,
        }

    def show_main_window(self, page: str | None = None) -> Any:
        params: dict[str, Any] = {"target": "main"}
        if page is not None:
            params["page"] = page
        return self.call("window.show", params).get("result")

    def _connect(self) -> BinaryIO:
        if os.name == "nt":
            # Windows exposes .NET NamedPipeServerStream through this file namespace.
            return open(rf"\\.\pipe\{self.pipe_name}", "r+b", buffering=0)

        endpoint = os.path.join(
            os.environ.get("TMPDIR") or "/tmp",
            f"CoreFxPipe_{self.pipe_name}",
        )
        sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        sock.settimeout(self.timeout)
        sock.connect(endpoint)
        return sock.makefile("rwb", buffering=0)

    def _readline(self, pipe: BinaryIO) -> bytes:
        deadline = time.monotonic() + self.timeout
        data = bytearray()
        while True:
            if time.monotonic() > deadline:
                raise TimeoutError("timed out waiting for SecRandom automation response")
            chunk = pipe.read(1)
            if not chunk:
                raise ConnectionError("SecRandom closed the automation pipe")
            if chunk == b"\n":
                return bytes(data).rstrip(b"\r")
            data.extend(chunk)
