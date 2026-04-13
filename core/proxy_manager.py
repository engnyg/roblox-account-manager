"""
代理測試與管理。
移植自 lib/lib.py testProxy()。
"""

from __future__ import annotations

import re
import httpx


def test_proxy(proxy: str) -> tuple[bool, str]:
    """
    測試代理是否可用。
    返回 (is_working, message)
    """
    if not proxy or not proxy.strip():
        return False, "Empty proxy"

    proxy = proxy.strip()

    # 自動補協議
    if not proxy.startswith(("http://", "https://", "socks4://", "socks5://")):
        if re.match(r"^\d+\.\d+\.\d+\.\d+:\d+$", proxy):
            proxy = "http://" + proxy
        else:
            return False, f"Invalid format: {proxy}"

    # 防注入
    if any(c in proxy for c in ["&", "|", ";", "$", "`", "(", ")", "<", ">"]):
        return False, f"Proxy contains invalid characters: {proxy}"

    try:
        transport = httpx.HTTPTransport(proxy=proxy)
        with httpx.Client(transport=transport, timeout=10) as client:
            r = client.get("http://www.google.com")
        if r.status_code == 200:
            return True, f"Proxy {proxy} is working"
        return False, f"Proxy returned status {r.status_code}"
    except httpx.TimeoutException:
        return False, f"Proxy {proxy} timed out"
    except httpx.ConnectError:
        return False, f"Proxy {proxy} connection failed"
    except Exception as e:
        return False, f"Proxy {proxy} test failed: {e}"


def filter_working_proxies(proxy_list: list[str]) -> list[str]:
    """過濾出可用代理。"""
    working = []
    for proxy in proxy_list:
        ok, msg = test_proxy(proxy)
        if ok:
            working.append(proxy)
        else:
            from loguru import logger
            logger.warning(msg)
    return working
