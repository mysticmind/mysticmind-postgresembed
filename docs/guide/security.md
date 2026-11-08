# Security

The library is made for local development and tests, and its defaults reflect that.

- **Localhost only, trust authentication.** The server listens on localhost and accepts local connections
  without a password; a password in a connection string is ignored. Do not use it to serve a network.
- **Verified downloads.** The binaries are checked against the SHA-256 Maven publishes next to them. A mirror
  that publishes none installs with a warning, or fails with `RequireChecksum = true`
  ([Mirrors and proxies](./cache#mirrors-and-proxies)).
- **Verified extensions.** Give an extension archive's `Sha256` and a mismatch fails the start
  ([Verify an archive](./extensions#verify-an-archive)).
- **Archives stay in their folder.** Extraction keeps every entry and every link inside the install folder.
- **Your user, not root.** Postgres runs as the current user and refuses to run as root or Administrator.
