OpenTransfer 1.2.11 Single-Instance Hotfix

FIXED

• OpenTransfer now allows only one running process per Windows session.
• Launching OpenTransfer again exits the new process and restores/focuses the existing window.
• Repeated launches no longer create duplicate tray icons.
• Installer checks the same application mutex to prevent parallel update instances.
