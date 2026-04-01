#!/bin/bash

# Shelly-ALPM Uninstaller
# Removes files installed by local-install.sh or web-install.sh

# Check for root privileges
if [ "$EUID" -ne 0 ]; then
  echo "Please run as root"
  exit 1
fi

INSTALL_DIR="/opt/starfish"

echo "Removing starfish installation..."

# Remove shelly-ui from /usr/bin (symlink from install.sh/local-install.sh OR binary from web-install.sh)
if [ -L /usr/bin/starfish ] || [ -f /usr/bin/sstarfish ]; then
    echo "Removing /usr/bin/starfish"
    rm -f /usr/bin/shelly-ui
fi

# Remove desktop entry
if [ -f /usr/share/applications/starfish.desktop ]; then
    echo "Removing desktop entry"
    rm -f /usr/share/applications/starfish.desktop
fi

if [ -f /usr/share/icons/hicolor/256x256/apps/starfish.png ]; then
    echo "Removing icon"
    rm -f /usr/share/icons/hicolor/256x256/apps/starfish.png
fi
# Remove installation directory
if [ -d "$INSTALL_DIR" ]; then
    echo "Removing installation directory: $INSTALL_DIR"
    rm -rf "$INSTALL_DIR"
fi

# Remove user-specific files
REAL_USER=${SUDO_USER:-$USER}
USER_HOME=$(getent passwd "$REAL_USER" | cut -d: -f6)

# Remove desktop shortcut
USER_DESKTOP="$USER_HOME/Desktop"
if [ -f "$USER_DESKTOP/starfish.desktop" ]; then
    echo "Removing desktop shortcut for user: $REAL_USER"
    rm -f "$USER_DESKTOP/starfish.desktop"
fi

echo "Uninstallation complete!"
