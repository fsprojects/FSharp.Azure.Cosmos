#!/usr/bin/env pwsh
param(
    # A tag of Azure/azure-cosmos-dotnet-v3, such as 3.62.0: the version of Microsoft.Azure.Cosmos in Directory.Packages.props.
    [Parameter(Mandatory)] [string] $Tag
)
# Copies the grammar of Cosmos DB SQL and the parser generated from it out of the Azure Cosmos DB .NET SDK at the given
# tag into this folder, replacing what is there. Run it when the SDK version changes, then build and run the tests.
#
# README.md of this folder and THIRD-PARTY-NOTICES.md at the root of the repository say which tag the files come from, and
# the notices say its commit as well. The script copies nothing until both name the tag and the commit it is run for, so
# that the files and what is said about them cannot part: change the documents first.
$ErrorActionPreference = 'Stop'
$repository = 'https://github.com/Azure/azure-cosmos-dotnet-v3'
$source = "https://raw.githubusercontent.com/Azure/azure-cosmos-dotnet-v3/$Tag/Microsoft.Azure.Cosmos/src/Query/Core/Parser"
$files = 'sql.g4', 'sqlLexer.cs', 'sqlParser.cs', 'IsqlVisitor.cs', 'sqlBaseVisitor.cs', 'IsqlListener.cs', 'sqlBaseListener.cs'

# The commit of the tag. An annotated tag lists the commit it points to on a second line, whose name ends with ^{}.
$refs = @(git ls-remote $repository "refs/tags/$Tag" "refs/tags/$Tag^{}")
if ($LASTEXITCODE -ne 0 -or $refs.Count -eq 0) {
    throw "$repository has no tag $Tag."
}
$commit = ($refs[-1] -split '\s+')[0]

$statements = @(
    @{ Document = Join-Path $PSScriptRoot 'README.md'; Texts = @("tag ``$Tag``") }
    @{ Document = Join-Path $PSScriptRoot '../../THIRD-PARTY-NOTICES.md'; Texts = @("tag ``$Tag``", $commit) }
)
foreach ($statement in $statements) {
    $text = Get-Content -Raw $statement.Document
    foreach ($expected in $statement.Texts) {
        if (-not $text.Contains($expected)) {
            throw "$($statement.Document) does not say '$expected'. Name the tag $Tag and its commit $commit in the documents first, then run this script again."
        }
    }
}

# Every file is downloaded before any of them is replaced, so a request that fails cannot leave this folder with the files
# of two versions under the name of one
$downloads = [ordered]@{}
foreach ($file in $files) {
    $downloads[$file] = (Invoke-WebRequest -Uri "$source/$file").RawContentStream.ToArray()
    "downloaded $file"
}

foreach ($file in $files) {
    [IO.File]::WriteAllBytes((Join-Path $PSScriptRoot $file), $downloads[$file])
    "copied $file"
}
