"""High-precision secret/token screening for SFT admission."""
from __future__ import annotations
import re
PATTERNS=(
("SECRET_OPENAI_KEY",re.compile(r"\bsk-[A-Za-z0-9]{20,}\b")),
("SECRET_GITHUB_TOKEN",re.compile(r"\b(?:ghp|gho|ghs|ghr)_[A-Za-z0-9]{20,}\b")),
("SECRET_GITHUB_PAT",re.compile(r"\bgithub_pat_[A-Za-z0-9_]{20,}\b")),
("SECRET_AWS_ACCESS_KEY",re.compile(r"\bAKIA[0-9A-Z]{16}\b")),
("SECRET_BEARER_TOKEN",re.compile(r"\bBearer\s+[A-Za-z0-9._~+/=-]{20,}\b",re.IGNORECASE)),
("SECRET_PRIVATE_KEY",re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----")),
("SECRET_ASSIGNMENT",re.compile(r"\b(?:api[_-]?key|access[_-]?token|auth[_-]?token|password|secret|token)\s*[:=]\s*[\"']?[A-Za-z0-9_./+~=-]{8,}",re.IGNORECASE)),)
def find_secrets(text:str)->list[tuple[str,str]]:
    findings=[]
    for code,pattern in PATTERNS:
        match=pattern.search(text)
        if match: findings.append((code,match.group(0)))
    return findings
def validate_row_secrets(row:dict)->list[str]:
    findings=[]
    for message in row.get("messages",[]) if isinstance(row.get("messages"),list) else []:
        content=message.get("content","") if isinstance(message,dict) else ""
        if isinstance(content,str): findings.extend(code for code,_ in find_secrets(content))
    return findings
