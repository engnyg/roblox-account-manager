from __future__ import annotations

import re
from dataclasses import dataclass, field
from datetime import datetime
from typing import Optional

import httpx

from core.constants import (
    AUTH_API, USERS_API, FRIENDS_API, AVATAR_API,
    ACCOUNT_API, ROBLOX_BASE, FOLLOW_PRIVACY,
)


@dataclass
class Account:
    security_token: str = ""
    username: str = ""
    user_id: int = 0
    alias: str = ""
    description: str = ""
    password: str = ""
    email: str = ""
    email_password: str = ""
    group: str = "Default"
    fields: dict[str, str] = field(default_factory=dict)
    last_use: Optional[datetime] = None
    last_attempted_refresh: Optional[datetime] = None
    browser_tracker_id: str = ""
    valid: bool = False

    # Runtime-only (not serialised)
    csrf_token: str = field(default="", repr=False)
    token_set: Optional[datetime] = field(default=None, repr=False)
    pin_unlocked: Optional[datetime] = field(default=None, repr=False)
    last_app_launch: Optional[datetime] = field(default=None, repr=False)

    # ------------------------------------------------------------------ #
    # HTTP helpers
    # ------------------------------------------------------------------ #

    def _cookies(self) -> dict:
        return {".ROBLOSECURITY": self.security_token}

    def _make_client(self, base_url: str) -> httpx.Client:
        return httpx.Client(
            base_url=base_url,
            cookies=self._cookies(),
            headers={"User-Agent": "Mozilla/5.0"},
            timeout=15,
            follow_redirects=True,
        )

    # ------------------------------------------------------------------ #
    # CSRF token
    # ------------------------------------------------------------------ #

    def get_csrf_token(self) -> tuple[bool, str]:
        """Return (success, token_or_error)."""
        try:
            with self._make_client(AUTH_API) as c:
                r = c.post(
                    "/v1/authentication-ticket/",
                    headers={"Referer": "https://www.roblox.com/games/4924922222/Brookhaven-RP"},
                )
            if r.status_code != 403:
                return False, f"[{r.status_code}] {r.text}"
            token = r.headers.get("x-csrf-token", "")
            if token:
                self.csrf_token = token
                self.token_set = datetime.now()
                self.last_use = datetime.now()
            return bool(token), token
        except Exception as e:
            return False, str(e)

    # ------------------------------------------------------------------ #
    # Auth ticket (for game launch)
    # ------------------------------------------------------------------ #

    def get_auth_ticket(self) -> tuple[bool, str]:
        ok, token = self.get_csrf_token()
        if not ok:
            return False, f"CSRF failed: {token}"
        try:
            with self._make_client(AUTH_API) as c:
                r = c.post(
                    "/v1/authentication-ticket/",
                    json={},
                    headers={
                        "X-CSRF-TOKEN": token,
                        "Referer": "https://www.roblox.com/games/4924922222/Brookhaven-RP",
                    },
                )
            ticket = r.headers.get("rbx-authentication-ticket", "")
            if not ticket:
                return False, f"[{r.status_code}] no ticket header. body={r.text[:200]}"
            return True, ticket
        except Exception as e:
            return False, str(e)

    # ------------------------------------------------------------------ #
    # Account info
    # ------------------------------------------------------------------ #

    def fetch_info(self) -> bool:
        """Populate username / user_id from Roblox API. Returns True on success."""
        try:
            with self._make_client(ROBLOX_BASE) as c:
                r = c.get("/my/account/json")
            if r.status_code != 200:
                return False
            data = r.json()
            self.username = data.get("Name", "")
            self.user_id = data.get("UserId", 0)
            self.valid = bool(self.username and self.user_id)
            self.last_use = datetime.now()
            return self.valid
        except Exception:
            return False

    async def get_robux(self) -> int:
        try:
            with self._make_client(ROBLOX_BASE) as c:
                r = c.get("/mobileapi/userinfo")
            return r.json().get("RobuxBalance", 0)
        except Exception:
            return 0

    # ------------------------------------------------------------------ #
    # PIN
    # ------------------------------------------------------------------ #

    def check_pin(self) -> bool:
        if self.pin_unlocked and datetime.now() < self.pin_unlocked:
            return True
        ok, _ = self.get_csrf_token()
        if not ok:
            return False
        try:
            with self._make_client(AUTH_API) as c:
                r = c.get("/v1/account/pin/", headers={"Referer": "https://www.roblox.com/"})
            if r.status_code == 200:
                info = r.json()
                if not info.get("isEnabled"):
                    return True
                unlock = info.get("unlockedUntil")
                if unlock and int(unlock) > 0:
                    return True
        except Exception:
            pass
        return False

    def unlock_pin(self, pin: str) -> bool:
        if len(pin) != 4:
            return False
        if self.check_pin():
            return True
        ok, token = self.get_csrf_token()
        if not ok:
            return False
        try:
            with self._make_client(AUTH_API) as c:
                r = c.post(
                    "/v1/account/pin/unlock",
                    data={"pin": pin},
                    headers={"X-CSRF-TOKEN": token, "Referer": "https://www.roblox.com/"},
                )
            if r.status_code == 200:
                info = r.json()
                if info.get("isEnabled") and int(info.get("unlockedUntil", 0)) > 0:
                    from datetime import timedelta
                    self.pin_unlocked = datetime.now() + timedelta(seconds=int(info["unlockedUntil"]))
                    return True
        except Exception:
            pass
        return False

    # ------------------------------------------------------------------ #
    # Privacy / password / email
    # ------------------------------------------------------------------ #

    def set_follow_privacy(self, privacy: int) -> bool:
        if not self.check_pin():
            return False
        ok, token = self.get_csrf_token()
        if not ok:
            return False
        value = FOLLOW_PRIVACY.get(privacy, "All")
        try:
            with self._make_client(ROBLOX_BASE) as c:
                r = c.post(
                    "/account/settings/follow-me-privacy",
                    data={"FollowMePrivacy": value},
                    headers={"X-CSRF-TOKEN": token, "Referer": "https://www.roblox.com/my/account"},
                )
            return r.status_code == 200
        except Exception:
            return False

    def change_password(self, current: str, new: str) -> tuple[bool, str]:
        if not self.check_pin():
            return False, "PIN is locked"
        ok, token = self.get_csrf_token()
        if not ok:
            return False, token
        try:
            with self._make_client(AUTH_API) as c:
                r = c.post(
                    "/v2/user/passwords/change",
                    data={"currentPassword": current, "newPassword": new},
                    headers={"X-CSRF-TOKEN": token, "Referer": "https://www.roblox.com/"},
                )
            if r.status_code == 200:
                self.password = new
                new_cookie = r.cookies.get(".ROBLOSECURITY")
                if new_cookie:
                    self.security_token = new_cookie
                return True, "Password changed"
            return False, f"Failed: {r.status_code} {r.text}"
        except Exception as e:
            return False, str(e)

    def change_email(self, password: str, new_email: str) -> tuple[bool, str]:
        if not self.check_pin():
            return False, "PIN is locked"
        ok, token = self.get_csrf_token()
        if not ok:
            return False, token
        try:
            with self._make_client(ACCOUNT_API) as c:
                r = c.post(
                    "/v1/email",
                    data={"password": password, "emailAddress": new_email},
                    headers={"X-CSRF-TOKEN": token, "Referer": "https://www.roblox.com/"},
                )
            if r.status_code == 200:
                return True, "Email changed"
            return False, f"Failed: {r.status_code}"
        except Exception as e:
            return False, str(e)

    def logout_other_sessions(self) -> tuple[bool, str]:
        if not self.check_pin():
            return False, "PIN is locked"
        ok, token = self.get_csrf_token()
        if not ok:
            return False, token
        try:
            with self._make_client(ROBLOX_BASE) as c:
                r = c.post(
                    "/authentication/signoutfromallsessionsandreauthenticate",
                    data={},
                    headers={"X-CSRF-TOKEN": token, "Referer": "https://www.roblox.com/"},
                )
            if r.status_code == 200:
                new_cookie = r.cookies.get(".ROBLOSECURITY")
                if new_cookie:
                    self.security_token = new_cookie
                return True, "Signed out of all other sessions"
            return False, f"Failed: {r.status_code}"
        except Exception as e:
            return False, str(e)

    # ------------------------------------------------------------------ #
    # Friends
    # ------------------------------------------------------------------ #

    def send_friend_request(self, target_user_id: int) -> bool:
        ok, token = self.get_csrf_token()
        if not ok:
            return False
        try:
            with self._make_client(FRIENDS_API) as c:
                r = c.post(
                    f"/v1/users/{target_user_id}/request-friendship",
                    headers={"X-CSRF-TOKEN": token},
                )
            return r.status_code == 200
        except Exception:
            return False

    # ------------------------------------------------------------------ #
    # Blocked users
    # ------------------------------------------------------------------ #

    def get_blocked_list(self) -> tuple[bool, dict]:
        if not self.check_pin():
            return False, {}
        try:
            with self._make_client(ACCOUNT_API) as c:
                r = c.get("/v1/users/get-detailed-blocked-users")
            return r.status_code == 200, r.json() if r.status_code == 200 else {}
        except Exception:
            return False, {}

    def block_user(self, user_id: int, unblock: bool = False) -> bool:
        if not self.check_pin():
            return False
        ok, token = self.get_csrf_token()
        if not ok:
            return False
        action = "unblock" if unblock else "block"
        try:
            with self._make_client(ACCOUNT_API) as c:
                r = c.post(f"/v1/users/{user_id}/{action}", headers={"X-CSRF-TOKEN": token})
            return r.status_code == 200
        except Exception:
            return False

    # ------------------------------------------------------------------ #
    # Display name / avatar
    # ------------------------------------------------------------------ #

    def set_display_name(self, display_name: str) -> tuple[bool, str]:
        ok, token = self.get_csrf_token()
        if not ok:
            return False, token
        try:
            with self._make_client(USERS_API) as c:
                r = c.patch(
                    f"/v1/users/{self.user_id}/display-names",
                    json={"newDisplayName": display_name},
                    headers={"X-CSRF-TOKEN": token},
                )
            if r.status_code == 200:
                return True, "Display name set"
            errors = r.json().get("errors", [{}])
            return False, errors[0].get("message", f"{r.status_code}")
        except Exception as e:
            return False, str(e)

    def set_avatar(self, avatar_json: dict) -> tuple[bool, str]:
        ok, token = self.get_csrf_token()
        if not ok:
            return False, token
        headers = {"X-CSRF-TOKEN": token}
        try:
            with self._make_client(AVATAR_API) as c:
                if "playerAvatarType" in avatar_json:
                    c.post("/v1/avatar/set-player-avatar-type",
                           json={"playerAvatarType": avatar_json["playerAvatarType"]},
                           headers=headers)
                scale = avatar_json.get("scales") or avatar_json.get("scale")
                if scale:
                    c.post("/v1/avatar/set-scales", json=scale, headers=headers)
                if "bodyColors" in avatar_json:
                    c.post("/v1/avatar/set-body-colors", json=avatar_json["bodyColors"], headers=headers)
                if "assets" in avatar_json:
                    r = c.post("/v2/avatar/set-wearing-assets",
                               json={"assets": avatar_json["assets"]},
                               headers=headers)
                    if r.status_code != 200:
                        return False, f"Assets failed: {r.status_code}"
            return True, "Avatar set"
        except Exception as e:
            return False, str(e)

    # ------------------------------------------------------------------ #
    # Custom fields
    # ------------------------------------------------------------------ #

    def get_field(self, name: str) -> str:
        return self.fields.get(name, "")

    def set_field(self, name: str, value: str):
        self.fields[name] = value

    def remove_field(self, name: str):
        self.fields.pop(name, None)

    # ------------------------------------------------------------------ #
    # Serialisation helpers
    # ------------------------------------------------------------------ #

    def to_dict(self) -> dict:
        return {
            "security_token": self.security_token,
            "username": self.username,
            "user_id": self.user_id,
            "alias": self.alias,
            "description": self.description,
            "password": self.password,
            "email": self.email,
            "email_password": self.email_password,
            "group": self.group,
            "fields": self.fields,
            "last_use": self.last_use.isoformat() if self.last_use else None,
            "last_attempted_refresh": self.last_attempted_refresh.isoformat() if self.last_attempted_refresh else None,
            "browser_tracker_id": self.browser_tracker_id,
        }

    @classmethod
    def from_dict(cls, data: dict) -> "Account":
        a = cls(
            security_token=data.get("security_token", ""),
            username=data.get("username", ""),
            user_id=data.get("user_id", 0),
            alias=data.get("alias", ""),
            description=data.get("description", ""),
            password=data.get("password", ""),
            email=data.get("email", ""),
            email_password=data.get("email_password", ""),
            group=data.get("group", "Default"),
            fields=data.get("fields", {}),
            browser_tracker_id=data.get("browser_tracker_id", ""),
        )
        if data.get("last_use"):
            try:
                a.last_use = datetime.fromisoformat(data["last_use"])
            except ValueError:
                pass
        a.valid = bool(a.username and a.user_id)
        return a

    def __repr__(self):
        return f"Account(username={self.username!r}, group={self.group!r}, valid={self.valid})"
