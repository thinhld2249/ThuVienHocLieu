import { describe, expect, it } from 'vitest'
import { formatDateTime, formatScore10, formatDate } from '../date'

describe('formatDateTime', () => {
  it('chuyển UTC sang giờ VN (UTC+7) dạng dd/MM/yyyy HH:mm', () => {
    // 02:30 UTC = 09:30 giờ VN
    expect(formatDateTime('2026-10-04T02:30:00Z')).toBe('04/10/2026 09:30')
  })

  it('rỗng → dấu gạch', () => {
    expect(formatDateTime(null)).toBe('—')
    expect(formatDateTime(undefined)).toBe('—')
  })
})

describe('formatDate', () => {
  it('dd/MM/yyyy theo giờ VN', () => {
    expect(formatDate('2026-12-31T18:30:00Z')).toBe('01/01/2027')
  })
})

describe('formatScore10', () => {
  it('dấu phẩy thập phân (spec §13)', () => {
    expect(formatScore10(8.75)).toBe('8,75')
    expect(formatScore10(10)).toBe('10')
  })

  it('rỗng → dấu gạch', () => {
    expect(formatScore10(null)).toBe('—')
  })
})
