"""
隨機密碼產生器。
符合 Roblox 密碼要求：8+ 字元、含大小寫字母與數字。
"""

import random
import string


def generate_password(length: int = 12) -> str:
    """
    產生符合 Roblox 規則的隨機密碼。
    規則：至少 8 字元、含大寫、小寫、數字、特殊字元。
    """
    if length < 8:
        length = 8

    upper = random.choice(string.ascii_uppercase)
    lower = random.choice(string.ascii_lowercase)
    digit = random.choice(string.digits)
    special = random.choice("!@#$%^&*")

    remaining_len = length - 4
    pool = string.ascii_letters + string.digits + "!@#$%^&*"
    remaining = [random.choice(pool) for _ in range(remaining_len)]

    chars = [upper, lower, digit, special] + remaining
    random.shuffle(chars)
    return "".join(chars)
