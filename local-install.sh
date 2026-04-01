#!/bin/bash

# Local Install Script for starfish-ALPM
# This script builds and installs starfish locally, similar to install.sh
# but starting from source code instead of pre-built binaries.

set -e  # Exit on any error

# Check for root privileges
if [ "$EUID" -ne 0 ]; then
  echo "Please run as root (use sudo)"
  exit 1
fi

INSTALL_DIR="/opt/starfish"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_CONFIG="Release"

echo "=========================================="
echo "starfish Local Install Script"
echo "=========================================="
echo ""

# Check if dotnet is installed
if ! command -v dotnet &> /dev/null; then
    echo "Error: .NET SDK is not installed. Please install .NET 10.0 SDK first."
    exit 1
fi

echo "Script directory: $SCRIPT_DIR"
echo "Install directory: $INSTALL_DIR"
echo ""

# Build starfish.Gtk
echo "Building starfish..."
cd "$SCRIPT_DIR"
dotnet publish -c $BUILD_CONFIG -r linux-x64 -o "$SCRIPT_DIR/publish/starfish" -p:InstructionSet=x86-64
echo "Starfish build complete."
echo ""

# Create installation directory
echo "Creating installation directory: $INSTALL_DIR"
mkdir -p "$INSTALL_DIR"


# Copy starfish.Gtk files (binary is named 'starfish-ui' due to AssemblyName)
echo "Copying starfish files to $INSTALL_DIR"
cp -r "$SCRIPT_DIR/publish/starfish/"* "$INSTALL_DIR/"


# Create symlinks in /usr/bin so commands are available on PATH
echo "Creating symlinks in /usr/bin..."
ln -sf "$INSTALL_DIR/starfish" /usr/bin/starfish

# Install icon to standard location
echo "Installing icon to standard location..."
mkdir -p /usr/share/icons/hicolor/256x256/apps
cp "$INSTALL_DIR/starfishlogo.png" /usr/share/icons/hicolor/256x256/apps/starfish.png

# Create desktop entry
echo "Creating desktop entry"
cat <<EOF > /usr/share/applications/starfish.desktop
[Desktop Entry]
Name=starfish
Comment=A Modern Arch Package Manager
Exec=/usr/bin/starfish-ui
Icon=starfish
Type=Application
Categories=System;Utility;
Terminal=false
EOF

# Clean up publish directory (optional - comment out to keep build artifacts)
echo "Cleaning up build artifacts..."
rm -rf "$SCRIPT_DIR/publish"

echo ""
echo "=========================================="
echo "Installation complete!"
echo "=========================================="
echo ""
echo "You can now:"
echo "  - Run the GUI: starfish-ui"
echo "  - Run the CLI: starfish"
echo "  - Notification Service: starfish-notifications"
echo "  - Find starfish in your application menu"
echo ""
