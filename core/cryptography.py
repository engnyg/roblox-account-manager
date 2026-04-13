"""
Argon2 + SecretBox encryption (PyNaCl).
Matches the C# Cryptography.cs format exactly so existing .dat files are compatible.
"""

import io
import nacl.pwhash
import nacl.secret
import nacl.utils

from core.constants import RAM_HEADER

_SALT_BYTES = 16   # Argon2 salt length used by libsodium
_NONCE_BYTES = nacl.secret.SecretBox.NONCE_SIZE  # 24
_KEY_BYTES = 32


def encrypt(content: str, password: bytes) -> bytes:
    """Encrypt a UTF-8 string; return the full binary blob."""
    if not content:
        raise ValueError("content must not be empty")

    salt = nacl.utils.random(_SALT_BYTES)
    key = _derive_key(password, salt)
    box = nacl.secret.SecretBox(key)
    nonce = nacl.utils.random(_NONCE_BYTES)
    ciphertext = box.encrypt(content.encode(), nonce).ciphertext  # strip nacl's own nonce prefix

    buf = io.BytesIO()
    buf.write(RAM_HEADER)
    buf.write(salt)
    buf.write(nonce)
    buf.write(ciphertext)
    return buf.getvalue()


def decrypt(data: bytes, password: bytes) -> str:
    """Decrypt a binary blob; return the plaintext string."""
    buf = io.BytesIO(data)

    header = buf.read(len(RAM_HEADER))
    if header != RAM_HEADER:
        raise ValueError("Invalid RAM header — wrong file or wrong password")

    salt = buf.read(_SALT_BYTES)
    nonce = buf.read(_NONCE_BYTES)
    ciphertext = buf.read()

    key = _derive_key(password, salt)
    box = nacl.secret.SecretBox(key)
    plaintext = box.decrypt(ciphertext, nonce)
    return plaintext.decode()


def _derive_key(password: bytes, salt: bytes) -> bytes:
    """Argon2id key derivation (matches C# PasswordHash.ArgonHashBinary Moderate)."""
    return nacl.pwhash.argon2id.kdf(
        _KEY_BYTES,
        password,
        salt,
        opslimit=nacl.pwhash.argon2id.OPSLIMIT_MODERATE,
        memlimit=nacl.pwhash.argon2id.MEMLIMIT_MODERATE,
    )
