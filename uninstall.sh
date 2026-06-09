#!/bin/bash
# LazyCaddy Uninstaller
# Removes the lazycaddy binary.
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

INSTALL_DIR="$HOME/.local/bin"

echo "lazycaddy Uninstaller"
echo ""

# Remove binary
if [ -f "$INSTALL_DIR/lazycaddy" ]; then
    rm "$INSTALL_DIR/lazycaddy"
    echo "✓ Removed $INSTALL_DIR/lazycaddy"
else
    echo "  Binary not found at $INSTALL_DIR/lazycaddy"
fi

# Remove uninstaller
if [ -f "$INSTALL_DIR/lazycaddy-uninstall.sh" ]; then
    rm "$INSTALL_DIR/lazycaddy-uninstall.sh"
fi

# Clean PATH from shell config
for RC in "$HOME/.bashrc" "$HOME/.zshrc"; do
    if [ -f "$RC" ] && grep -q "$INSTALL_DIR" "$RC" 2>/dev/null; then
        sed -i "\|$INSTALL_DIR|d" "$RC"
        echo ""
        echo "✓ Removed PATH entry from $RC"
    fi
done

echo ""
echo "✓ lazycaddy uninstalled."
echo ""
echo "  Note: ~/.config/lazycaddy/ (snapshots) was left in place. Remove it manually if desired."
