"""Small, dependency-free adapter for SecRandom automation IPC v2."""

from .client import (
    AutomationClient,
    AutomationError,
    AutomationException,
    DrawChannel,
    DrawOptions,
)

__all__ = [
    "AutomationClient",
    "AutomationError",
    "AutomationException",
    "DrawChannel",
    "DrawOptions",
]
