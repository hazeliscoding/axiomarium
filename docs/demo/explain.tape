# The axm explain demo, on the demo scenario in scenarios/demo. .github/workflows/demo.yml renders it with
# VHS, using the axm release on NuGet: https://github.com/charmbracelet/vhs
Output docs/demo/explain.gif

Require axm

Set Shell "bash"
Set FontFamily "JetBrains Mono"
Set FontSize 14
Set LineHeight 1.2
Set Width 1560
Set Height 760
Set Padding 28
Set TypingSpeed 40ms
Set Theme { "name": "KAIRO", "black": "#101418", "red": "#ff5f6d", "green": "#8fd18f", "yellow": "#ffb454", "blue": "#6cb6ff", "magenta": "#f0569b", "cyan": "#5fd7d7", "white": "#e9e7e1", "brightBlack": "#6b7280", "brightRed": "#ff7a85", "brightGreen": "#a6e3a1", "brightYellow": "#ffd28a", "brightBlue": "#8cc8ff", "brightMagenta": "#ff7ab8", "brightCyan": "#87e7e7", "brightWhite": "#ffffff", "background": "#101418", "foreground": "#e9e7e1", "selection": "#2a2f36", "cursor": "#f0569b" }

# The demo repo and its fake home, copied outside the checkout, so no file above them is read and ~ is
# the demo's home.
Hide
Type "export PS1='$ ' && rm -rf /tmp/shop /tmp/shop-home && cp -r scenarios/demo/repo /tmp/shop && cp -r scenarios/demo/home /tmp/shop-home && mkdir /tmp/shop/.git && export HOME=/tmp/shop-home && cd /tmp/shop && clear"
Enter
Show

Type "axm explain src/api/orders.cs"
Sleep 500ms
Enter
Sleep 8s

Hide
Type "clear"
Enter
Show

Type "axm explain src/api/orders.cs --diff"
Sleep 500ms
Enter
Sleep 5s
