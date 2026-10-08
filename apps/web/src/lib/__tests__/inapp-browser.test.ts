import { describe, expect, it } from 'vitest'
import { isInAppBrowser } from '../inapp-browser'

describe('isInAppBrowser', () => {
  it('nhận ra webview Zalo', () => {
    expect(isInAppBrowser('Mozilla/5.0 (Linux; Android 13) ZABrowser/10.5 Zalo/10.5')).toBe(true)
  })

  it('nhận ra Facebook in-app', () => {
    expect(isInAppBrowser('Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Mobile/15E148 [FBAN/FBIOS;FVV100]')).toBe(true)
  })

  it('Chrome/Safari thường không phải in-app', () => {
    expect(isInAppBrowser('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126 Safari/537.36')).toBe(false)
  })
})
