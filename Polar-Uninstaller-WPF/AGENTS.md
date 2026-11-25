# Release notes

For every Polar Uninstaller update, document all user-facing changes that ship in that version in `Assets/ReleaseNotes.json`. Maintain equivalent English and Arabic entries. Use separate bullets for distinct features, improvements, and fixes, and verify the current version shows them in What's New before packaging the executable and installer.

# Local test builds

After each app change and its tests, run `Build-TestPackage.ps1` to rebuild the standalone EXE, Setup, and `SHA256SUMS.txt` together. Provide links to all three files in the new test build folder. Keep existing release packages and publish to GitHub only when requested.
