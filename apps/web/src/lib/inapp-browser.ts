/** §3.1: phát hiện webview in-app (Zalo/Facebook…) — Google chặn đăng nhập trong webview. */
export function isInAppBrowser(userAgent: string = navigator.userAgent): boolean {
  return /zabrowser|zalo|fbios|fbaov|instagram|whatsapp|msgr/i.test(userAgent)
}
