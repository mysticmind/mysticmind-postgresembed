---
layout: home

hero:
  name: MysticMind.PostgresEmbed
  text: A real PostgreSQL server in your .NET tests
  tagline: Start any Postgres version from code, extensions included, on Windows, Linux and macOS. No Docker, no local install.
  image:
    src: /logo.png
    alt: MysticMind.PostgresEmbed, an elephant standing on a window of code
  actions:
    - theme: brand
      text: Get Started
      link: /guide/getting-started
    - theme: alt
      text: In Tests and CI
      link: /guide/testing
    - theme: alt
      text: View on GitHub
      link: https://github.com/mysticmind/mysticmind-postgresembed

features:
  - title: Any version
    details: An exact version such as 17.6.0, or a spec such as 17 or latest that resolves to the newest one published for your platform.
    link: /guide/versions
    linkText: Choosing a version
  - title: Minimal, small downloads
    details: Minimal binaries with just what a server needs, much smaller than the official downloads. Downloaded once, checked against the published SHA-256, and shared by every project on the machine.
    link: /guide/cache
    linkText: Downloads and cache
  - title: Every platform
    details: Windows x64, Linux x64 and arm64 with glibc or musl (Alpine), macOS x64 and arm64. The library picks the right binaries.
    link: /guide/introduction
    linkText: Requirements
  - title: Keep a server around
    details: A named instance keeps its data between runs, and a second process or the pgembed CLI attaches to it in milliseconds instead of starting another.
    link: /guide/named-instances
    linkText: Named instances
  - title: Extensions
    details: The extensions bundled with Postgres, plus extension archives the library downloads, verifies and installs for you.
    link: /guide/extensions
    linkText: Extensions
  - title: Made for tests
    details: One server per test class or assembly with an xUnit fixture, a cache step for CI, and no Postgres left behind when a test run ends.
    link: /guide/testing
    linkText: Tests and CI
---
