<#
.SYNOPSIS
    Posts a social/drafts file to Telegram through the user's bot: the "## Telegram" section (optionally with the
    draft's media), then the "## Telegram poll" section as a native poll. Either section may be left out.

.DESCRIPTION
    Three modes, safest first:
      (default)  Preview: prints what would be sent. No network call.
      -Check     Asks Telegram whether the bot can post there. Sends nothing (a topic check shows a brief "typing").
      -Send      Posts and prints the links.

    Poll section format: the question on the first line(s), then 2-10 options as "- option" lines.

    The bot token is read from TELEGRAM_BOT_TOKEN (this process, or the Windows user environment, so a new
    value works without restarting anything). It is never taken as an argument and never printed.

.EXAMPLE
    pwsh social/post-telegram.ps1 -Draft social/drafts/2026-11-01-v0.8.2-beta-release.md -Check
    pwsh social/post-telegram.ps1 -Draft social/drafts/2026-11-04-hot-take.md -Channel @nerd9ja_hub -Send
#>
param(
    [Parameter(Mandatory)][string]$Draft,
    # Default: the Updates topic of the @Paperbunkr group (Paperbunkr posts). Fan posts: -Channel @nerd9ja_hub.
    [string]$Channel = '@Paperbunkr',
    # Topic (forum thread) id in a group with Topics on; 0 = the General topic. From a topic link
    # https://t.me/Paperbunkr/<topic id> or a message link https://t.me/Paperbunkr/<topic id>/<message id>.
    # Left out: 13 (Updates) for @Paperbunkr, none for anything else.
    [int]$Topic = -1,
    # A video or image to send with the text as its caption. Left out: the draft's "media:" line (path from the repo root).
    [string]$Media,
    [switch]$Check,
    [switch]$Send
)

$ErrorActionPreference = 'Stop'
$content = Get-Content -Raw -Encoding utf8 -LiteralPath $Draft

# The draft's own "telegram:" line (e.g. "telegram: @nerd9ja_hub") picks the destination unless -Channel is given.
if (-not $PSBoundParameters.ContainsKey('Channel')) {
    $t = [regex]::Match($content, '(?m)^telegram:\s*(@\w+)')
    if ($t.Success) { $Channel = $t.Groups[1].Value }
}
if ($Topic -lt 0) { $Topic = if ($Channel -ieq '@Paperbunkr') { 13 } else { 0 } }
function Get-Section([string]$Heading) {
    $m = [regex]::Match($content, "(?ms)^## $([regex]::Escape($Heading))\s*$\r?\n(.*?)(?=^## |\z)")
    if ($m.Success) { $m.Groups[1].Value.Trim() } else { '' }
}

# --- The text: everything under "## Telegram" up to the next "## " heading.
$text = Get-Section 'Telegram'

# --- The poll: question lines, then "- option" lines.
$poll = $null
$pollRaw = Get-Section 'Telegram poll'
if ($pollRaw) {
    $lines = $pollRaw -split '\r?\n' | Where-Object { $_.Trim() -and $_ -notmatch '^\s*```' }
    $question = (($lines | Where-Object { $_ -notmatch '^\s*-\s+' }) -join ' ').Trim()
    $options = @($lines | Where-Object { $_ -match '^\s*-\s+' } | ForEach-Object { ($_ -replace '^\s*-\s+', '').Trim() })
    if (-not $question) { throw 'The poll has no question line' }
    if ($question.Length -gt 300) { throw "Poll question is $($question.Length) characters; the limit is 300" }
    if ($options.Count -lt 2 -or $options.Count -gt 10) { throw "A poll needs 2 to 10 options; this one has $($options.Count)" }
    foreach ($o in $options) { if ($o.Length -gt 100) { throw "Poll option over 100 characters: $o" } }
    $poll = @{ question = $question; options = $options }
}
if (-not $text -and -not $poll) { throw "No '## Telegram' or '## Telegram poll' section in $Draft" }

# --- Media: -Media, else the draft's "media:" front-matter line. Sent with the text as its caption.
if (-not $Media) {
    $m = [regex]::Match($content, '(?m)^media:\s*(\S+)')
    if ($m.Success -and $m.Groups[1].Value -ne 'none') { $Media = $m.Groups[1].Value }
}
$mediaKind = $null
$mediaIsUrl = $Media -match '^https?://'
if ($mediaIsUrl) {
    # A public URL: Telegram fetches it itself (photos up to 5 MB, videos up to 20 MB this way).
    $mediaKind = switch -regex (([uri]$Media).AbsolutePath) { '\.(jpe?g|png|webp)$' { 'photo' } '\.gif$' { 'animation' } default { 'video' } }
    $mediaMB = 'url'
} elseif ($Media) {
    if (-not [IO.Path]::IsPathRooted($Media)) { $Media = Join-Path (Split-Path $PSScriptRoot -Parent) $Media }
    if (-not (Test-Path -LiteralPath $Media)) { throw "Media file not found: $Media" }
    $mediaKind = switch -regex ([IO.Path]::GetExtension($Media)) { '^\.(jpe?g|png|webp)$' { 'photo' } '^\.gif$' { 'animation' } default { 'video' } }
    $mediaMB = [math]::Round((Get-Item -LiteralPath $Media).Length / 1MB, 1)
    if ($mediaMB -gt 50) { throw "Media is $mediaMB MB; bots can upload at most 50 MB" }
}
$limit = if ($mediaKind) { 1024 } else { 4096 }
if ($text.Length -gt $limit) { throw "Telegram text is $($text.Length) characters; the limit is $limit$(if ($mediaKind) { ' for a caption' })" }

if (-not $Check -and -not $Send) {
    Write-Output ("Channel: $Channel" + $(if ($Topic -gt 0) { ", topic $Topic" }))
    if ($mediaKind) { Write-Output "Media:   $mediaKind, $mediaMB MB, $Media" }
    if ($text) {
        Write-Output "Length:  $($text.Length) / $limit"
        Write-Output '----------------------------------------'
        Write-Output $text
    }
    if ($poll) {
        Write-Output '---------------- poll ------------------'
        Write-Output $poll.question
        $poll.options | ForEach-Object { Write-Output "  ( ) $_" }
    }
    return
}

# --- The token, without ever echoing it.
$token = $env:TELEGRAM_BOT_TOKEN
if (-not $token) { $token = [Environment]::GetEnvironmentVariable('TELEGRAM_BOT_TOKEN', 'User') }
if (-not $token) { throw 'TELEGRAM_BOT_TOKEN is not set. Save it once with the command in .claude/skills/paperbunkr-social/SKILL.md.' }

function Get-TelegramError($ErrorRecord, [string]$Method) {
    # Report Telegram's own description; scrub the token from anything else before it is shown.
    $detail = $ErrorRecord.ErrorDetails.Message
    $reason = if ($detail) { try { ($detail | ConvertFrom-Json).description } catch { $detail } } else { $ErrorRecord.Exception.Message }
    "Telegram $Method failed: " + ($reason -replace [regex]::Escape($token), '***')
}

function Invoke-Telegram([string]$Method, [hashtable]$Body) {
    $json = $Body | ConvertTo-Json -Depth 5 -Compress
    try {
        $response = Invoke-RestMethod -Method Post -Uri "https://api.telegram.org/bot$token/$Method" `
            -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json))
    } catch { throw (Get-TelegramError $_ $Method) }
    return $response.result
}

function Invoke-TelegramUpload([string]$Method, [hashtable]$Form) {
    try {
        $response = Invoke-RestMethod -Method Post -Uri "https://api.telegram.org/bot$token/$Method" -Form $Form
    } catch { throw (Get-TelegramError $_ $Method) }
    return $response.result
}

function Get-Link($Sent) {
    if (-not $Sent.chat.username) { return "message id $($Sent.message_id)" }
    if ($Topic -gt 0) { return "https://t.me/$($Sent.chat.username)/$Topic/$($Sent.message_id)" }
    "https://t.me/$($Sent.chat.username)/$($Sent.message_id)"
}

if ($Check) {
    $me = Invoke-Telegram 'getMe' @{}
    $chat = Invoke-Telegram 'getChat' @{ chat_id = $Channel }
    $member = Invoke-Telegram 'getChatMember' @{ chat_id = $Channel; user_id = $me.id }
    # "Post messages" is a channel-only admin right; in a group or supergroup any admin can send, and a plain member
    # can unless restricted.
    $canPost = if ($chat.type -eq 'channel') {
        ($member.status -eq 'creator') -or ($member.status -eq 'administrator' -and $member.can_post_messages)
    } else {
        ($member.status -in 'creator', 'administrator', 'member') -or ($member.status -eq 'restricted' -and $member.can_send_messages)
    }
    Write-Output "Bot:      @$($me.username)"
    Write-Output "Channel:  $($chat.title) (@$($chat.username), $($chat.type)$(if ($chat.is_forum) { ', Topics on' }))"
    if ($chat.is_forum -and $Topic -gt 0) {
        # A "typing" action is the only way to test a topic id without posting; it fails if the topic doesn't exist.
        $null = Invoke-Telegram 'sendChatAction' @{ chat_id = $Channel; message_thread_id = $Topic; action = 'typing' }
        Write-Output "Topic:    $Topic (exists)"
    } elseif ($chat.is_forum) {
        Write-Output 'Topic:    General'
    }
    Write-Output "Bot role: $($member.status)"
    Write-Output ("Can post: " + ($(if ($canPost) { 'yes' } else { 'NO - make the bot an admin with "Post messages"' })))
    if ($mediaKind) { Write-Output "Media:    $mediaKind, $mediaMB MB / 50, ready" }
    if ($text) { Write-Output "Text:     $($text.Length) / $limit characters, ready" }
    if ($poll) { Write-Output "Poll:     $($poll.options.Count) options, ready" }
    return
}

if ($text -or $mediaKind) {
    if ($mediaKind -and $mediaIsUrl) {
        $body = @{ chat_id = $Channel; caption = $text; $mediaKind = $Media }
        if ($Topic -gt 0) { $body.message_thread_id = $Topic }
        $sent = Invoke-Telegram (@{ photo = 'sendPhoto'; video = 'sendVideo'; animation = 'sendAnimation' }[$mediaKind]) $body
    } elseif ($mediaKind) {
        $form = @{ chat_id = $Channel; caption = $text; $mediaKind = (Get-Item -LiteralPath $Media) }
        if ($mediaKind -eq 'video') { $form.supports_streaming = 'true' }
        if ($Topic -gt 0) { $form.message_thread_id = "$Topic" }
        $sent = Invoke-TelegramUpload (@{ photo = 'sendPhoto'; video = 'sendVideo'; animation = 'sendAnimation' }[$mediaKind]) $form
    } else {
        $body = @{ chat_id = $Channel; text = $text }
        if ($Topic -gt 0) { $body.message_thread_id = $Topic }
        $sent = Invoke-Telegram 'sendMessage' $body
    }
    Write-Output "Posted to $Channel"
    Write-Output "Link: $(Get-Link $sent)"
}

if ($poll) {
    # Channels only allow anonymous polls; keep them anonymous everywhere for consistency.
    $body = @{
        chat_id      = $Channel
        question     = $poll.question
        options      = @($poll.options | ForEach-Object { @{ text = $_ } })
        is_anonymous = $true
    }
    if ($Topic -gt 0) { $body.message_thread_id = $Topic }
    $sentPoll = Invoke-Telegram 'sendPoll' $body
    Write-Output "Poll posted to $Channel"
    Write-Output "Link: $(Get-Link $sentPoll)"
}
