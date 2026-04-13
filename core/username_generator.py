"""
使用者名稱產生器。
直接移植自現有 lib/lib.py 中的 UsernameGenerator 類別與 generateUsername()。
"""

from __future__ import annotations

import os
import random
from typing import Optional

from core.constants import get_resource_path


class UsernameGenerator:
    # SOURCE: https://github.com/mrsobakin/pungen
    CONSONANTS = "bcdfghjklmnpqrstvwxyz"
    VOWELS = "aeiou"

    CONS_WEIGHTED = ("tn", "rshd", "lfcm", "gypwb", "vbjxq", "z")
    VOW_WEIGHTED = ("eao", "iu")
    DOUBLE_CONS = ("he", "re", "ti", "ti", "hi", "to", "ll", "tt", "nn", "pp", "th", "nd", "st", "qu")
    DOUBLE_VOW = ("ee", "oo", "ei", "ou", "ai", "ea", "an", "er", "in", "on", "at", "es", "en", "of", "ed", "or", "as")

    def __init__(self, min_length: int, max_length: Optional[int] = None):
        self.min_length = min_length
        self.max_length = max_length or min_length

    def generate(self) -> str:
        username, is_double, num_length = "", False, 0

        is_consonant = random.randrange(10) > 0
        length = random.randrange(self.min_length, self.max_length + 1)

        if random.randrange(5) == 0:
            num_length = random.randrange(3) + 1
            if length - num_length < 2:
                num_length = 0

        letter_length = max(1, length - num_length)
        for j in range(letter_length):
            if username:
                if username[-1] in self.CONSONANTS:
                    is_consonant = False
                elif username[-1] in self.VOWELS:
                    is_consonant = True
            if not is_double:
                if random.randrange(8) == 0 and len(username) < letter_length - 1:
                    is_double = True
                if is_consonant:
                    username += self._get_consonant(is_double)
                else:
                    username += self._get_vowel(is_double)
                is_consonant = not is_consonant
            else:
                is_double = False

        if random.randrange(2) == 0:
            username = username[:1].upper() + username[1:]

        for _ in range(num_length):
            username += str(random.randrange(10))

        return username

    def _get_consonant(self, is_double: bool) -> str:
        if is_double:
            return random.choice(self.DOUBLE_CONS)
        i = random.randrange(100)
        if i < 40:
            w = 0
        elif i < 65:
            w = 1
        elif i < 80:
            w = 2
        elif i < 90:
            w = 3
        elif i < 97:
            w = 4
        else:
            return self.CONS_WEIGHTED[5]
        return self.CONS_WEIGHTED[w][random.randrange(len(self.CONS_WEIGHTED[w]))]

    def _get_vowel(self, is_double: bool) -> str:
        if is_double:
            return random.choice(self.DOUBLE_VOW)
        w = 0 if random.randrange(100) < 70 else 1
        return self.VOW_WEIGHTED[w][random.randrange(len(self.VOW_WEIGHTED[w]))]


def generate_scrambled(min_len: int = 10, max_len: int = 15) -> str:
    return UsernameGenerator(min_len, max_len).generate()


def generate_structured() -> str:
    """Verb + Noun + Adjective + 2-digit number."""
    try:
        with open(get_resource_path("lib/verbs.txt"), encoding="utf-8") as f:
            verbs = f.read().split()
        with open(get_resource_path("lib/nouns.txt"), encoding="utf-8") as f:
            nouns = f.read().split()
        with open(get_resource_path("lib/adjectives.txt"), encoding="utf-8") as f:
            adjectives = f.read().split()
        if not (verbs and nouns and adjectives):
            raise ValueError("Empty word lists")
        return random.choice(verbs) + random.choice(nouns) + random.choice(adjectives) + str(random.randint(10, 99))
    except Exception:
        return generate_scrambled()


def create_username(
    name_format: Optional[str] = None,
    scrambled: bool = True,
    counter: int = 0,
    max_attempts: int = 100,
) -> str:
    """
    Find an available Roblox username.
    - name_format: fixed prefix → "<prefix>_<counter>"
    - scrambled=True: phonetic random
    - scrambled=False: structured (verb+noun+adj+num)
    """
    from core.roblox_api import validate_username

    for attempt in range(max_attempts):
        if name_format:
            username = f"{name_format}_{counter + attempt}"
        elif scrambled:
            username = generate_scrambled()
        else:
            username = generate_structured()

        if validate_username(username):
            return username

    return generate_scrambled()
