"""
頭像隨機化。
移植自 lib/lib.py customization()。
"""

from __future__ import annotations

import asyncio
import random

from loguru import logger


async def randomize_avatar(tab) -> bool:
    """
    在已登入的 DrissionPage tab 上隨機化頭像。
    返回 True 表示成功。
    """
    try:
        tab.listen.start("https://avatar.roblox.com/v1/avatar-inventory?sortOption=recentAdded&pageLimit=50")
        tab.get("https://www.roblox.com/my/avatar")
        result = tab.listen.wait(timeout=10)
        if not result:
            return False
        content = result.response.body
        tab.listen.stop()

        asset_dict: dict[str, list] = {}
        for item in content.get("avatarInventoryItems", []):
            cat = item.get("itemCategory", {})
            asset_type = cat.get("itemSubType")
            if asset_type:
                asset_dict.setdefault(asset_type, []).append(item)

        for asset_type, assets in asset_dict.items():
            asset = random.choice(assets)
            try:
                for li in tab.ele(".hlist item-cards-stackable").eles("tag:li"):
                    if li.ele("tag:a").attr("data-item-name") == asset.get("itemName"):
                        li.ele("tag:a").click()
                        break
            except Exception as e:
                logger.warning(f"Could not click asset {asset.get('itemName', '?')}: {e}")

        body_type = random.choice(range(0, 101, 5))
        _set_body_type(tab, body_type)
        return True

    except Exception as e:
        logger.warning(f"Avatar customization failed: {e}")
        return False


def _set_body_type(tab, value: int):
    from DrissionPage import errors
    try:
        tab.run_js_loaded(f'document.getElementById("body type-scale").value = {value};')
        tab.run_js_loaded('document.getElementById("body type-scale").dispatchEvent(new Event("input"));')
    except errors.JavaScriptError:
        tab.run_js_loaded(f'''
            var slider = document.querySelector('input[aria-label="Body Type Scale"]');
            if (slider) {{
                var muiSlider = slider.closest('.MuiSlider-root');
                var rect = muiSlider.getBoundingClientRect();
                var pct = {value} / 100;
                var tx = rect.left + rect.width * pct;
                var ty = rect.top + rect.height / 2;
                muiSlider.dispatchEvent(new MouseEvent('mousedown', {{bubbles:true, clientX:tx, clientY:ty, button:0}}));
                var setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value").set;
                setter.call(slider, {value});
                slider.dispatchEvent(new Event('input', {{bubbles:true}}));
                slider.dispatchEvent(new Event('change', {{bubbles:true}}));
                muiSlider.dispatchEvent(new MouseEvent('mouseup', {{bubbles:true, clientX:tx, clientY:ty, button:0}}));
            }}
        ''')
