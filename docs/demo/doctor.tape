# The terminal demo in the README. .github/workflows/demo.yml renders it with VHS, using the axm
# release on NuGet: https://github.com/charmbracelet/vhs
Output docs/demo/doctor.gif

Require axm

Set Shell "bash"
Set FontFamily "JetBrains Mono"
Set FontSize 18
Set LineHeight 1.2
Set Width 1200
Set Height 880
Set Padding 28
Set TypingSpeed 40ms
Set Theme { "name": "KAIRO", "black": "#101418", "red": "#ff5f6d", "green": "#8fd18f", "yellow": "#ffb454", "blue": "#6cb6ff", "magenta": "#f0569b", "cyan": "#5fd7d7", "white": "#e9e7e1", "brightBlack": "#6b7280", "brightRed": "#ff7a85", "brightGreen": "#a6e3a1", "brightYellow": "#ffd28a", "brightBlue": "#8cc8ff", "brightMagenta": "#ff7ab8", "brightCyan": "#87e7e7", "brightWhite": "#ffffff", "background": "#101418", "foreground": "#e9e7e1", "selection": "#2a2f36", "cursor": "#f0569b" }

# A copy of the repo's vault, so the demo's edit never touches the checkout.
Hide
Type "export PS1='$ ' && rm -rf /tmp/vault && mkdir /tmp/vault && cp -r agents skills hooks policies experiments /tmp/vault && cd /tmp/vault && clear"
Enter
Show

Type "axm list"
Sleep 500ms
Enter
Sleep 3.5s

# Claim a maturity level the asset hasn't earned.
Type "sed -i 's/maturity: experimental/maturity: tested/' agents/determinism-auditor/asset.yaml"
Sleep 600ms
Enter
Sleep 800ms

Type "axm doctor"
Sleep 500ms
Enter
Sleep 6s
