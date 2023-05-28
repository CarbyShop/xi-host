$company = "Host"
$trademark = ""
$cultureCode = ""

$date = [System.DateTime]::UtcNow
$revision = [System.Math]::Round((($date.Hour * 60 * 60) + ($date.Minute * 60) + $date.Second) * 0.75)
if ($revision -lt [System.UInt16]::MinValue) { $revision = [System.UInt16]::MinValue }
if ($revision -gt [System.UInt16]::MaxValue) { $revision = [System.UInt16]::MaxValue }
$dateString = Get-Date $date -Format "yyyy.M.d"
$dateString = $dateString + ".$revision"

$year = Get-Date $date -Format "yyyy"
$copyright = "Copyright © $year $company"

$product = "XI Host"
$description = "$product is a set of .NET libraries and applications that support hosting components of an XI game server."

$assemblyInfoFiles = [System.IO.Directory]::GetFiles($args[0], "AssemblyInfo.cs", [System.IO.SearchOption]::AllDirectories)
$configuration = $args[1]

if ([System.String]::Compare($configuration, "Release")) { return }

foreach ($file in $assemblyInfoFiles)
{
    if ($file.Contains("clrzmq")) { continue }

    $array = $file.Split([System.IO.Path]::DirectorySeparatorChar)
    $title = $array[$array.Length - 3]

    $contents = [System.IO.File]::ReadAllText($file)
    $contents = $contents -replace         'Title\(".*"\)',         "Title(`"$title`")"
    $contents = $contents -replace   'Description\(".*"\)',   "Description(`"$description`")"
    $contents = $contents -replace 'Configuration\(".*"\)', "Configuration(`"$configuration`")"
    $contents = $contents -replace       'Company\(".*"\)',       "Company(`"$company`")"
    $contents = $contents -replace       'Product\(".*"\)',       "Product(`"$product`")"
    $contents = $contents -replace     'Copyright\(".*"\)',     "Copyright(`"$copyright`")"
    $contents = $contents -replace     'Trademark\(".*"\)',     "Trademark(`"$trademark`")"
    $contents = $contents -replace       'Culture\(".*"\)',       "Culture(`"$cultureCode`")"
    $contents = $contents -replace       'Version\(".*"\)',       "Version(`"$dateString`")"
    [System.IO.File]::WriteAllText($file, $contents, [System.Text.Encoding]::UTF8)
}
