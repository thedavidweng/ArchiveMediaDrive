.PHONY: test verify-tree

test:
	cd tools/reference-cli && python3 -m unittest discover -s tests -v

verify-tree:
	python3 -m json.tool contracts/source.schema.json >/dev/null
	python3 -m json.tool contracts/raw-node.schema.json >/dev/null
	python3 -m json.tool contracts/runtime-request.schema.json >/dev/null
	python3 -m json.tool runtime/rclone/manifest.json >/dev/null
	! find plugins/kodi -type f \( -name '*.so' -o -name '*.dll' -o -name '*.exe' -o -name '*.pyo' -o -name '*.pyc' \) | grep .
