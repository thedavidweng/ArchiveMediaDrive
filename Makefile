.PHONY: ci build test test-dotnet test-python test-kodi verify-tree format restore-locked

ci: restore-locked build test format verify-tree

build:
	dotnet build ArchiveMediaDrive.sln

restore-locked:
	dotnet restore --locked-mode ArchiveMediaDrive.sln

format:
	dotnet format --verify-no-changes

test: test-dotnet test-python test-kodi

test-dotnet:
	dotnet test ArchiveMediaDrive.sln --configuration Release

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
