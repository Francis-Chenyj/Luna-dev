# Security / 安全问题

This is a development project. No security-support period or audit certification is implied. Prefer the latest reviewed local build; the public pre-release may lag behind it.

Do not put secrets, raw personal logs or a working exploit against other users in a public issue. If GitHub offers **Report a vulnerability** in this repository's Security tab, use that private channel. If it is unavailable, open a minimal issue asking the maintainer to establish a private reporting channel, without exploit details or sensitive attachments. This document does not claim private reporting is enabled.

请提供受影响版本、操作系统、最小复现、预期影响和已做的脱敏处理。无需发送账号、令牌、完整缓存或签名私钥。维护者确认接收后再通过双方约定的私密渠道分享必要细节。

Relevant boundaries include the loopback HTTP service, package ownership and path validation, symlinks/junctions, update metadata signatures, archive extraction, startup recovery and uninstall data retention. Tests must use isolated folders. An update-metadata RSA signature verifies the update feed; it does not provide Windows Authenticode publisher identity.

Maintainers should reproduce in isolation, add a regression, verify supported recovery paths and document affected/fixed versions before coordinating disclosure. Third-party distribution evidence is tracked separately in [release status](../release-status.json).
