.PHONY: test test-dotnet test-python test-kodi verify-tree

test: test-dotnet test-python test-kodi verify-tree

test-dotnet:
	dotnet test shared/dotnet/ArchiveMediaDrive.Core.Tests/ArchiveMediaDrive.Core.Tests.csproj --configuration Release
	dotnet test plugins/jellyfin/ArchiveMediaDrive.Jellyfin.Tests/ArchiveMediaDrive.Jellyfin.Tests.csproj --configuration Release

test-python:
	cd tools/reference-cli && python3 -m unittest discover -s tests -v

test-kodi:
	cd plugins/kodi && python3 -m unittest discover -s tests -v

verify-tree:
	python3 -m json.tool contracts/source.schema.json >/dev/null
	python3 -m json.tool contracts/raw-node.schema.json >/dev/null
	python3 -m json.tool contracts/runtime-request.schema.json >/dev/null
	python3 -m json.tool runtime/rclone/manifest.json >/dev/null
	python3 -m json.tool contracts/fixtures/sources.json >/dev/null
	python3 -m json.tool contracts/fixtures/ia/manifest.json >/dev/null
	! find plugins/kodi -type f -not -path '*/__pycache__/*' \( -name '*.so' -o -name '*.dll' -o -name '*.exe' -o -name '*.pyo' -o -name '*.pyc' \) | grep .
