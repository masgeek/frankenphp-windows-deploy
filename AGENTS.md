# AGENTS.md

This file provides guidance to AI coding agents when working with code in this repository.

## Project

PHP Manager: a WPF desktop app (.NET 10, `net10.0-windows`, Windows x64 only) for managing PHP versions, extensions (Redis, SQL Server, Xdebug), php.ini, CA certs and FrankenPHP (as a Windows service via Servy). The only project is `PhpManager/` (assembly name `php-manager`). There are no tests; the `UnitTests` workflow only restores, builds and publishes.

## Commands

```powershell
dotnet build PhpManager/PhpManager.csproj -c Release
dotnet run --project PhpManager/PhpManager.csproj
dotnet publish PhpManager/PhpManager.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

## Release flow

- `ci.yml` (workflow `CI`) restores, builds and publishes on pushes to `main`/`develop` and PRs. There are no tests. The `publish/` output is cached by a hash of `PhpManager/**` plus the SDK version; on a hit restore/build/publish are skipped (caches are only saved on success, and GitHub scopes them per branch, so develop/main caches serve PRs into them).
- `next-release.yml` (`Create next release`) runs after CI succeeds on `develop` and opens a PR into `main` (label `bot-release`, title `Release: <head commit subject>`) as the munywele-bot App. Merging that PR to `main` is what starts the release chain below.
- `bump-and-tag.yml` (`BumpVersion`) runs after CI succeeds on `main` (push events only). If `PhpManager/` changed since the last tag it tags the next version (no prefix, e.g. `1.0.1`), creates a **draft** release with the munywele-bot GitHub App token (`vars.APP_CLIENT_ID`, `secrets.APP_PRIVATE_KEY`), then dispatches `release.yml` with the new version. Releases must stay drafts until the zip is attached: GitHub release immutability locks assets on publish.
- `release.yml` (`Publish Release`) is `workflow_dispatch` only (draft releases don't fire `release: published`). It checks out the tag, builds with `-p:Version=<tag>`, smoke-checks the exe version, attests provenance, uploads `php-manager-<version>-win-x64.zip` to the draft, and as the last step publishes it (`--draft=false --latest`). All release writes use the App token. It refuses to modify an already-published release. Don't hand-edit version properties in the csproj.

- **Release notes** are built by `.github/scripts/release-notes.ps1` (run by `release.yml` just before publishing; GitHub's auto-generated notes are not used). It collects the PRs merged since the previous tag and writes a user-facing `What's new` section from each PR's `## Release note` section (see `.github/pull_request_template.md`; blank falls back to the PR title, `none` marks an internal change). CI, docs, dependency and `none` PRs go into a collapsed `Maintenance` block, followed by a Download block (SHA-256 and `gh attestation verify`). `pr-title.yml` requires PR titles like `feat(scope): ...` / `fix: ...` (a leading emoji is fine) and labels them; the script classifies by label, then title prefix. `release.yml` also asks GitHub Models (`actions/ai-inference` pinned to v2.1.1, since v3+ only supports the Copilot CLI; uses `GITHUB_TOKEN` with `models: read`, no API key) for a short highlights paragraph at the top of What's new. It is advisory: it is skipped when there are no user-facing changes, rejected by sanity checks if malformed, and a failed or rate-limited call (`continue-on-error`) leaves the deterministic notes untouched. `.github/release.yml` only affects GitHub's manual "Generate release notes" button.

## Architecture

- **UI shell**: `MainWindow` builds one instance of each page (`*Page.xaml` UserControls, keyed by nav button `Tag`) up front and swaps them into `ContentArea`. A new page needs registration in `MainWindow.xaml.cs` plus a nav button in `MainWindow.xaml`. Code-behind only, no MVVM.
- **`PhpService.cs`** (~1600 lines, static): all business logic. PHP versions live in `<BasePath>\<version>` (default `C:\PHP`); the active version is a switch/junction, with `frankenphp` as a special "version". It also covers php.ini and extension handling, download/zip helpers, Xdebug/Redis/SQL Server installers, FrankenPHP install and service management through the Servy CLI, and the elevated-PATH helpers.
- **Elevation**: system PATH changes relaunch the exe with `--set-system-path <dir>` or `--set-frankenphp-path`. `App.xaml.cs` `OnStartup` handles these args, shows a message box and exits without opening the main window.
- **Persistence sits next to the exe** (`AppContext.BaseDirectory`):
  - `settings.json` holds the PHP and FrankenPHP paths (`PhpService.LoadSettings`/`SaveSettings`).
  - `urls.db` (SQLite) holds the `urls` table (`UrlDatabase`) and the generic JSON `app_config` key/value table (`AppConfig`).
  - `frankenphp-services.db` (SQLite) holds the `services` table (`FrankenPhpDatabase`).
- **URLs are data, not constants**: every download, catalog and documentation URL is seeded into `urls.db` from the `Defaults` array in `UrlDatabase.cs` and is editable in the UI (`UrlsPage`). Code reads them through the typed accessors in `Urls.cs`, never hardcoded. Download templates use tokens (`{0}`, `{version}`, `{package}`, `{path}`). The catalog scraper regex is derived from the primary download template so the two stay in sync. When adding a downloadable, add a default row in `UrlDatabase` and an accessor in `Urls`.
- **Logging**: `LogWindow.LogWarn` and related static methods feed a shared log window. Long operations take `IProgress<string>` and report through it.
- Extension installers (`InstallRedisAsync`, `InstallXdebugAsync`, `InstallSqlServerAsync`) detect the PHP build (version, NTS/TS, arch, compiler) to pick the right package. `XdebugEnvironment`/`XdebugConfig` model that for the Xdebug wizard.
