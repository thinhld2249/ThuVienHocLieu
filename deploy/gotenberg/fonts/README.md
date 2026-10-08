# Font cho Gotenberg

TTF của Microsoft core fonts (Times New Roman, Arial, Verdana — đủ bộ regular/bold/italic/bold-italic), nguồn `ttf-mscorefonts-installer` (Debian contrib, redistributable). Cam kết vào repo để build image **không cần mạng** (sandbox/CI/VM đều như nhau).

Trên máy Windows thiếu font, nạp lại bằng lệnh:

```powershell
foreach ($f in 'times','timesbd','timesi','timesbi','arial','arialbd','ariali','arialbi','verdana','verdanab','verdanai','verdanaz') { Copy-Item "C:\Windows\Fonts\$f.ttf" "deploy\gotenberg\fonts\" -Force }
```
