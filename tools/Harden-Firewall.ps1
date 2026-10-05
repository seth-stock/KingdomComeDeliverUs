# Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
# belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    Blocks the first game's remote-console port (TCP 4600) from every computer except this one.
.DESCRIPTION
    The mod switches on the engine's remote console, which listens on every network address and has no allow-list
    (docs/KCD1-MODDING.md section 3). Windows Firewall does not filter loopback traffic, so the agent on this machine still
    reaches it, while a block rule keeps every other machine out. Needs administrator rights: it asks for them itself.
    -Add (default) creates the rule; -Remove deletes it; -Check prints whether it exists.
#>
[CmdletBinding()]
param([switch] $Add, [switch] $Remove, [switch] $Check)

$ruleName = 'Kingdom Come Deliver Us: block remote console (4600) from other computers'
$exists = [bool](Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)
if ($Check) { if ($exists) { 'present' } else { 'absent' }; exit 0 }

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    $a = if ($Remove) { '-Remove' } else { '-Add' }
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" $a"
    exit 0
}
if ($Remove) {
    if ($exists) { Remove-NetFirewallRule -DisplayName $ruleName; 'removed' } else { 'nothing to remove' }
    exit 0
}
if (-not $exists) {
    # every IPv4 address EXCEPT 127.0.0.0/8: the rule cannot touch the agent on this machine, whatever Windows does with loopback
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Block -Protocol TCP -LocalPort 4600 -Profile Any -RemoteAddress '0.0.0.0-126.255.255.255','128.0.0.0-255.255.255.255' | Out-Null
    'rule added'
} else { 'rule already present' }
