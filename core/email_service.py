"""
Mail.tm 臨時信箱服務。
移植自 lib/lib.py generateEmail() / fetchVerification()。
"""

from __future__ import annotations

import asyncio
import random
from typing import Optional

import httpx
from pymailtm import MailTm, Account as MailAccount

from core.username_generator import generate_scrambled


class EmailService:
    def __init__(self):
        self._mailtm = MailTm()
        self._account: Optional[MailAccount] = None

    async def generate_email(self, password: str = "Qing762.chy") -> tuple[str, str, str, object]:
        """
        建立臨時信箱。
        返回 (address, password, token, account_response)
        """
        max_retries = 3
        for attempt in range(max_retries):
            try:
                domains = self._mailtm._get_domains_list()
                if not domains:
                    raise RuntimeError("No mail.tm domains available")

                domain = random.choice(domains)
                username = generate_scrambled(6, 12).lower()
                address = f"{username}@{domain}"

                async with httpx.AsyncClient(timeout=15) as client:
                    resp = await client.post(
                        "https://api.mail.tm/accounts",
                        json={"address": address, "password": password},
                    )
                    if resp.status_code != 201:
                        raise RuntimeError(f"Account creation failed: {resp.status_code}")

                    account_data = resp.json()
                    if "id" not in account_data:
                        raise RuntimeError("No account ID in response")

                    token_resp = await client.post(
                        "https://api.mail.tm/token",
                        json={"address": address, "password": password},
                    )
                    if token_resp.status_code != 200:
                        raise RuntimeError(f"Token request failed: {token_resp.status_code}")

                    token_data = token_resp.json()
                    if "token" not in token_data:
                        raise RuntimeError("No token in response")

                    return address, password, token_data["token"], resp

            except Exception as e:
                if attempt < max_retries - 1:
                    await asyncio.sleep(5)
                else:
                    raise RuntimeError(f"Failed after {max_retries} attempts: {e}") from e

        raise RuntimeError("Unreachable")

    def fetch_messages(self, address: str, password: str, account_response: object) -> list:
        """取得收件匣訊息列表。"""
        if self._account is None:
            self._account = MailAccount(account_response, address, password)
        return self._account.get_messages()

    def reset(self):
        """重置 account 狀態（每次產生新信箱後呼叫）。"""
        self._account = None
