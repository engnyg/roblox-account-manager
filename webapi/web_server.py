"""
本地 Web API 伺服器（port 7963）。
對應 C# Classes/WebServer.cs + AccountManager.cs 的 WebServer 路由處理。
"""

from __future__ import annotations

import json
from typing import TYPE_CHECKING, Callable

from aiohttp import web
from loguru import logger

from core.constants import WEB_API_PORT

if TYPE_CHECKING:
    from core.account import Account


class WebApiServer:
    def __init__(
        self,
        port: int = WEB_API_PORT,
        password: str = "",
        get_accounts: Callable[[], list["Account"]] = lambda: [],
        get_selected: Callable[[], "Account | None"] = lambda: None,
    ):
        self._port = port
        self._password = password
        self._get_accounts = get_accounts
        self._get_selected = get_selected
        self._app = web.Application(middlewares=[self._auth_middleware])
        self._runner: web.AppRunner | None = None
        self._setup_routes()

    def _auth_check(self, request: web.Request) -> bool:
        if not self._password:
            return True
        return request.headers.get("password", "") == self._password or \
               request.rel_url.query.get("password", "") == self._password

    @web.middleware
    async def _auth_middleware(self, request: web.Request, handler):
        if not self._auth_check(request):
            return web.Response(status=401, text="Unauthorized")
        return await handler(request)

    def _setup_routes(self):
        self._app.router.add_get("/GetCSRFToken", self._get_csrf)
        self._app.router.add_get("/GetAccounts", self._list_accounts)
        self._app.router.add_post("/JoinServer", self._join_server)
        self._app.router.add_get("/GetCurrentAccount", self._get_current)

    async def _get_csrf(self, request: web.Request) -> web.Response:
        username = request.rel_url.query.get("username", "")
        account = self._find_account(username)
        if not account:
            return web.Response(status=404, text="Account not found")
        ok, token = account.get_csrf_token()
        if ok:
            return web.Response(text=token)
        return web.Response(status=403, text=token)

    async def _list_accounts(self, request: web.Request) -> web.Response:
        accounts = self._get_accounts()
        data = [{"username": a.username, "group": a.group, "valid": a.valid} for a in accounts]
        return web.Response(text=json.dumps(data), content_type="application/json")

    async def _join_server(self, request: web.Request) -> web.Response:
        try:
            body = await request.json()
        except Exception:
            return web.Response(status=400, text="Invalid JSON")
        username = body.get("username", "")
        place_id = int(body.get("placeId", 0))
        job_id = body.get("jobId", "")
        account = self._find_account(username)
        if not account:
            return web.Response(status=404, text="Account not found")
        from manager.game_launcher import join_server
        ok, msg = join_server(account, place_id, job_id)
        if ok:
            return web.Response(text="Success")
        return web.Response(status=500, text=msg)

    async def _get_current(self, request: web.Request) -> web.Response:
        acc = self._get_selected()
        if not acc:
            return web.Response(status=404, text="No account selected")
        return web.Response(
            text=json.dumps({"username": acc.username, "userId": acc.user_id}),
            content_type="application/json",
        )

    def _find_account(self, username: str) -> "Account | None":
        if not username:
            return self._get_selected()
        for a in self._get_accounts():
            if a.username.lower() == username.lower():
                return a
        return None

    async def start(self):
        self._runner = web.AppRunner(self._app)
        await self._runner.setup()
        site = web.TCPSite(self._runner, "localhost", self._port)
        await site.start()
        logger.info(f"Web API server running on http://localhost:{self._port}")

    async def stop(self):
        if self._runner:
            await self._runner.cleanup()
            logger.info("Web API server stopped")
