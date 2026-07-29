.PHONY: test verify-tree

test:
	cd tools/reference-cli && python -m unittest discover -s tests -v

verify-tree:
	python -m json.tool contracts/source.schema.json >/dev/null
	python -m json.tool contracts/raw-node.schema.json >/dev/null
	python -m json.tool runtime/rclone/manifest.json >/dev/null
	! find plugins/kodi -type f \( -name '*.so' -o -name '*.dll' -o -name '*.exe' -o -name '*.pyo' -o -name '*.pyc' \) | grep .
