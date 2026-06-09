#!/bin/bash
# LazyCaddy Installer
# Downloads and installs the latest release from GitHub.
# Usage: curl -fsSL https://raw.githubusercontent.com/nickprotop/lazycaddy/master/install.sh | bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

set -e

REPO="nickprotop/lazycaddy"
INSTALL_DIR="$HOME/.local/bin"

echo "Installing lazycaddy..."

# Detect OS and architecture (Linux only — lazycaddy manages a Caddy admin API,
# which is a Linux-server tool).
OS=$(uname -s)
ARCH=$(uname -m)

if [ "$OS" != "Linux" ]; then
    echo "Error: Unsupported OS: $OS"
    echo "lazycaddy ships Linux binaries only. On another OS, build from source with 'dotnet publish'."
    exit 1
fi

case "$ARCH" in
    x86_64)  BINARY="lazycaddy-linux-x64" ;;
    aarch64) BINARY="lazycaddy-linux-arm64" ;;
    *) echo "Error: Unsupported Linux architecture: $ARCH"; exit 1 ;;
esac

# Get latest release info
echo "Fetching latest release..."
RELEASE_INFO=$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest")
TAG=$(echo "$RELEASE_INFO" | grep '"tag_name"' | head -1 | sed 's/.*"tag_name": "\(.*\)".*/\1/')
VERSION="${TAG#v}"

if [ -z "$TAG" ]; then
    echo "Error: Could not determine latest release."
    exit 1
fi

echo "Latest version: $VERSION"

# Download binary
DOWNLOAD_URL="https://github.com/$REPO/releases/download/$TAG/$BINARY"
echo "Downloading $BINARY..."

mkdir -p "$INSTALL_DIR"
curl -fsSL "$DOWNLOAD_URL" -o "$INSTALL_DIR/lazycaddy"
chmod +x "$INSTALL_DIR/lazycaddy"

# Download uninstaller
curl -fsSL "https://raw.githubusercontent.com/$REPO/master/uninstall.sh" -o "$INSTALL_DIR/lazycaddy-uninstall.sh"
chmod +x "$INSTALL_DIR/lazycaddy-uninstall.sh"

# Ensure PATH
if [[ ":$PATH:" != *":$INSTALL_DIR:"* ]]; then
    SHELL_RC=""
    if [ -f "$HOME/.zshrc" ]; then
        SHELL_RC="$HOME/.zshrc"
    elif [ -f "$HOME/.bashrc" ]; then
        SHELL_RC="$HOME/.bashrc"
    fi

    if [ -n "$SHELL_RC" ]; then
        if ! grep -q "$INSTALL_DIR" "$SHELL_RC" 2>/dev/null; then
            echo "export PATH=\"$INSTALL_DIR:\$PATH\"" >> "$SHELL_RC"
            echo "Added $INSTALL_DIR to PATH in $SHELL_RC"
        fi
    fi
fi

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  ✓ lazycaddy v$VERSION installed!"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "  Binary:  $INSTALL_DIR/lazycaddy"
echo ""
echo "  Run:     lazycaddy                          # http://localhost:2019"
echo "           lazycaddy --url http://host:2019    # a different admin API"
echo "  Remove:  lazycaddy-uninstall.sh"
echo ""
if [[ ":$PATH:" != *":$INSTALL_DIR:"* ]]; then
    echo "  Note: Restart your shell or run:"
    echo "    source ~/.bashrc  (or ~/.zshrc)"
fi
