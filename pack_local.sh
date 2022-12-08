#!/usr/bin/env bash

# Packs or removes a local package from the local nuget feed.
# This allows one to test a project as a nuget package without publishing it.

# Usage: pack_local.sh <options> [pack|remove] [project path/csproj file]
# Options: -h|--help
#          -f|--feed <feed path> (default: ./.local_feed)

PROJECT_DIR=""
DEFAULT_FEED="./.local_feed"

feed="$DEFAULT_FEED"

get_csproj() {
    local path="${PROJECT_DIR}$1"

    if [ -d "$path" ]; then
        local csproj=$(find "$path" -name "*.csproj" | head -n 1)
        if [ -z "$csproj" ]; then
            echo "No csproj file found in $path"
            exit 1
        fi
        echo "$csproj"
    elif [ -f "$path" ]; then
        echo "$path"
    else
        echo "Invalid path: $path"
        exit 1
    fi
}

main() {
    while [[ $# -gt 0 ]]; do
        local key="$1"
        case $key in
            -h|--help)
                echo "Usage: pack_local.sh <options> [pack|remove] [project path/csproj file]"
                echo "Options: -h|--help"
                echo "         -f|--feed <feed path> (default: $DEFAULT_FEED)"
                exit 0
                ;;
            -f|--feed)
                feed="$2"
                shift
                ;;
            *)
                break
                ;;
        esac
        shift
    done

    if [ $# -lt 2 ]; then
        echo "Usage: pack_local.sh <options> [pack|remove] [project path/csproj file]"
        exit 1
    fi

    local command="$1"
    local project_name="$2"

    if [ "$command" == "pack" ]; then
        remove "$2"
        pack "$2"
    elif [ "$command" == "remove" ]; then
        remove "$2"
    else
        echo "Invalid command: $command"
        exit 1
    fi
}

pack() {
    local csproj=$(get_csproj "$1")
    local package=$(dotnet pack "$csproj" -o "$feed" | grep -oP "(?<=Successfully created package ').*?(?=')")

    if [ -z "$package" ]; then
        echo "Failed to pack $csproj"
        exit 1
    fi

    echo "Packed: $package"
}

remove() {
    local project_name="$1"
    local package=$(readlink -f "$(find "$feed" -name "$project_name.*.nupkg" | head -n 1)")

    if [ -f "$package" ]; then
        rm "$package"
        echo "Removed: $package"
    fi
}

main "$@"
