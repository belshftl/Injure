#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

resolve_profiler_path() {
	local os
	local name
	local arch
	case "$(uname -s)" in
		Darwin*) os="osx"; name="libinjureclrprof.dylib" ;;
		Linux*)  os="linux"; name="libinjureclrprof.so" ;;
		*)       echo "unsupported platform '$(uname -s)'" >&2; exit 2 ;;
	esac
	case "$(uname -m)" in
		x86_64)        arch="x64" ;;
		aarch64|arm64) arch="arm64" ;;
		*)             echo "unsupported architecture '$(uname -m)'" >&2; exit 2 ;;
	esac
	local candidates=(
		"$script_dir/$name"
		"$script_dir/../../native/pack/$os-$arch/$name"
	)
	for candidate in "${candidates[@]}"; do
		if [[ -f "$candidate" ]]; then
			realpath "$candidate"
			return 0
		fi
	done
	return 1
}

if [[ -n "${CORECLR_PROFILER_PATH:-}" ]]; then
	profiler_path="$CORECLR_PROFILER_PATH"
elif profiler_path="$(resolve_profiler_path)"; then
	:
else
	echo "could not find the profiler library; set CORECLR_PROFILER_PATH explicitly, or build" >&2
	echo "the profiler crate into native/pack/<rid>/, usually by using the makefile" >&2
	exit 2
fi

if [[ $# -eq 0 ]]; then
	echo "usage: $0 command [args ...]" >&2
	echo "  e.g. $0 dotnet run" >&2
	exit 2
fi

export CORECLR_ENABLE_PROFILING=1
export CORECLR_PROFILER='{c2401225-e6e4-434b-b7fa-e9c849bb6271}'
export CORECLR_PROFILER_PATH="$profiler_path"

echo "running with profiler: $CORECLR_PROFILER_PATH" >&2
exec "$@"
