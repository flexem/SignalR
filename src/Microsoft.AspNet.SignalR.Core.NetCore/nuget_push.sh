#!/bin/bash
dotnet build -c Release || exit 1
nuget push "`ls -Art bin/Release/*.nupkg | grep -v 'symbols.nupkg$' | tail -n 1`" -Source https://nexus.flexem.net/repository/nuget-flexem/
