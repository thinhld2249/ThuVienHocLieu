import { describe, expect, it } from 'vitest'
import { slugFromUrl, contentUrl } from '../slug'

describe('slugFromUrl', () => {
  it('tách slug từ URL /tai-lieu/:slug-:id', () => {
    expect(slugFromUrl('/tai-lieu/ke-hoach-chu-nhiem-lop-5a-42')).toBe('ke-hoach-chu-nhiem-lop-5a')
    expect(slugFromUrl('/bai-tap/bai-tap-cuoi-tuan-5-toan-7')).toBe('bai-tap-cuoi-tuan-5-toan')
  })

  it('slug chứa số vẫn tách đúng id', () => {
    expect(slugFromUrl('/tai-lieu/de-khao-sat-5-99')).toBe('de-khao-sat-5')
  })

  it('không có id thì trả nguyên segment', () => {
    expect(slugFromUrl('/tai-lieu/ke-hoach')).toBe('ke-hoach')
  })
})

describe('contentUrl', () => {
  it('đúng dạng /{slug}-{id}', () => {
    expect(contentUrl('tai-lieu', 'ke-hoach', 42)).toBe('/tai-lieu/ke-hoach-42')
    expect(contentUrl('bai-tap', 'btct', 7)).toBe('/bai-tap/btct-7')
  })
})
