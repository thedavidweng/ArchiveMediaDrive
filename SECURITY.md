# Security Policy

Report vulnerabilities privately through GitHub Security Advisories.

Security-sensitive surfaces include:

- source URL and identifier parsing;
- Internet Archive credentials;
- rclone runtime download and extraction;
- subprocess argument construction;
- mount lifecycle and permissions;
- logs containing remote URLs or authorization data.

ArchiveMediaDrive opens no network listener in its supported default modes. Rclone RC is invoked only with `--loopback`. Managed mounts are read-only and plugin-owned.
