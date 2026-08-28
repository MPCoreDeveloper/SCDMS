#!/usr/bin/env bash
# ============================================================================
# SCDMS installer for Linux and macOS (per-user, no root required)
#
# One-liner:
#   curl -fsSL https://raw.githubusercontent.com/MPCoreDeveloper/SCDMS/main/install/install.sh | bash
#
# Options:
#   --version v1.0.0   install a specific version (default: latest release)
#   --uninstall        remove SCDMS from this machine
#
# The installer is idempotent: re-running it performs an in-place update.
# Downloads are verified against the SHA256SUMS.txt published with each release.
# ============================================================================
set -euo pipefail

REPO="MPCoreDeveloper/SCDMS"
INSTALL_DIR="$HOME/.local/share/scdms"
BIN_DIR="$HOME/.local/bin"
VERSION=""
UNINSTALL=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version)   VERSION="$2"; shift 2 ;;
        --uninstall) UNINSTALL=true; shift ;;
        --help|-h)   grep '^#' "$0" | head -n 14; exit 0 ;;
        *) echo "Unknown argument: $1"; exit 1 ;;
    esac
done

log() { echo "==> $*"; }

if [[ "$UNINSTALL" = true ]]; then
    log "Uninstalling SCDMS..."
    pkill -x scdms 2>/dev/null || true
    rm -rf "$INSTALL_DIR"
    rm -f "$BIN_DIR/scdms" "$BIN_DIR/scdms-open"
    rm -f "$HOME/.local/share/applications/scdms.desktop"
    rm -f "$HOME/Applications/SCDMS.command"
    log "SCDMS removed. Your databases and settings remain in \$HOME/.local/share/SCDMS."
    exit 0
fi

# ── Detect platform ─────────────────────────────────────────────────────────
OS="$(uname -s)"
ARCH="$(uname -m)"
case "$OS" in
    Linux)  RID_OS="linux" ;;
    Darwin) RID_OS="osx" ;;
    *) echo "Unsupported OS: $OS"; exit 1 ;;
esac
case "$ARCH" in
    x86_64|amd64) RID_ARCH="x64" ;;
    arm64|aarch64) RID_ARCH="arm64" ;;
    *) echo "Unsupported architecture: $ARCH"; exit 1 ;;
esac
RID="${RID_OS}-${RID_ARCH}"

# ── Resolve version ─────────────────────────────────────────────────────────
if [[ -z "$VERSION" ]]; then
    log "Resolving latest SCDMS release..."
    VERSION="$(curl -fsSL --proto '=https' --proto-redir '=https' -H 'User-Agent: SCDMS-Installer' \
        "https://api.github.com/repos/$REPO/releases/latest" \
        | grep '"tag_name"' | sed -E 's/.*"v?([^"]+)".*/\1/')"
fi
VERSION="${VERSION#v}"
log "Installing SCDMS v$VERSION ($RID) ..."

ASSET="scdms_${VERSION}_${RID}.tar.gz"
BASE_URL="https://github.com/$REPO/releases/download/v$VERSION"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

# ── Download + verify ───────────────────────────────────────────────────────
log "[1/4] Downloading $ASSET ..."
curl -fsSL --proto '=https' --proto-redir '=https' -o "$TMP_DIR/$ASSET" "$BASE_URL/$ASSET"
curl -fsSL --proto '=https' --proto-redir '=https' -o "$TMP_DIR/SHA256SUMS.txt" "$BASE_URL/SHA256SUMS.txt"

log "[2/4] Verifying SHA256 checksum ..."
EXPECTED="$(grep "$ASSET" "$TMP_DIR/SHA256SUMS.txt" | awk '{print $1}')"
if [[ -z "$EXPECTED" ]]; then
    echo "No checksum found for $ASSET in SHA256SUMS.txt"; exit 1
fi
if command -v sha256sum >/dev/null 2>&1; then
    ACTUAL="$(sha256sum "$TMP_DIR/$ASSET" | awk '{print $1}')"
else
    ACTUAL="$(shasum -a 256 "$TMP_DIR/$ASSET" | awk '{print $1}')"
fi
if [[ "$ACTUAL" != "$EXPECTED" ]]; then
    echo "Checksum mismatch! Expected $EXPECTED, got $ACTUAL"; exit 1
fi
log "      Checksum OK."

# ── Stop running instance ───────────────────────────────────────────────────
log "[3/4] Stopping running SCDMS instances ..."
pkill -x scdms 2>/dev/null || true

# ── Install ─────────────────────────────────────────────────────────────────
log "[4/4] Installing to $INSTALL_DIR ..."
rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR" "$BIN_DIR"
tar -xzf "$TMP_DIR/$ASSET" -C "$INSTALL_DIR"
chmod +x "$INSTALL_DIR/scdms"

ln -sf "$INSTALL_DIR/scdms" "$BIN_DIR/scdms"

# Launcher: starts the server and opens the browser.
cat > "$BIN_DIR/scdms-open" <<'EOF'
#!/usr/bin/env bash
nohup scdms >/dev/null 2>&1 &
sleep 2
URL="https://localhost:5443"
if command -v xdg-open >/dev/null 2>&1; then
    xdg-open "$URL" >/dev/null 2>&1 || true
elif command -v open >/dev/null 2>&1; then
    open "$URL" >/dev/null 2>&1 || true
else
    echo "SCDMS running at $URL"
fi
EOF
chmod +x "$BIN_DIR/scdms-open"

if [[ "$RID_OS" = "linux" ]]; then
    mkdir -p "$HOME/.local/share/applications"
    cat > "$HOME/.local/share/applications/scdms.desktop" <<EOF
[Desktop Entry]
Name=SCDMS
Comment=Sharp Core Database Management System
Exec=$BIN_DIR/scdms-open
Terminal=false
Type=Application
Categories=Development;Database;
EOF
else
    mkdir -p "$HOME/Applications"
    cat > "$HOME/Applications/SCDMS.command" <<EOF
#!/usr/bin/env bash
exec "$BIN_DIR/scdms-open"
EOF
    chmod +x "$HOME/Applications/SCDMS.command"
fi

echo ""
echo "===================================================="
echo "  SCDMS v$VERSION installed successfully!"
echo ""
echo "  Start:  scdms-open     (server + browser)"
echo "  CLI:    scdms          (start the server)"
echo "          scdms --update (check for updates)"
echo ""
echo "  Open:   https://localhost:5443"
echo "  Note:   first launch uses a self-signed localhost"
echo "          certificate; accept the browser warning once."
if [[ ":$PATH:" != *":$BIN_DIR:"* ]]; then
    echo ""
    echo "  PATH:   $BIN_DIR is not on your PATH yet."
    echo "          Add this to your shell profile:"
    echo "            export PATH=\"\$HOME/.local/bin:\$PATH\""
fi
echo "  Data:   \$HOME/.local/share/SCDMS"
echo "===================================================="
