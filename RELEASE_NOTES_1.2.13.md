OpenTransfer 1.2.13 Failed Transfer Retry

NEW

• Transfer History now provides Retry Failed (N) for entries with persisted failures.
• Transfer completion summary now provides direct Retry Failed and View Failed Files actions.
• Only failed files are retried; the full job is not repeated.
• Download retries preserve the original remote path structure and local destination.
• Upload retries use the original Android destination and report missing local sources.
• Retry results are stored as new history entries for auditing and further retry if needed.
• Original-device validation prevents retrying a history job against the wrong Android device.
