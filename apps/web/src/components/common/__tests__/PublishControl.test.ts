import { describe, expect, it } from 'vitest'
import dayjs from 'dayjs'
import {
  publishStateLabel,
  schedulePresetValues,
} from '@/components/common/PublishControl'

describe('publishStateLabel', () => {
  it('map trạng thái hiển thị (spec §4.4)', () => {
    expect(publishStateLabel('Hidden')).toBe('Đang ẩn')
    expect(publishStateLabel('Visible')).toBe('Đang hiện')
    expect(publishStateLabel('ScheduledUpcoming')).toBe('Sắp mở')
    expect(publishStateLabel('ScheduledOpen')).toBe('Đang mở')
    expect(publishStateLabel('Closed')).toBe('Đã đóng')
  })
})

describe('schedulePresetValues', () => {
  it('weekend: T6 17:00 → CN 21:00', () => {
    const { from, until } = schedulePresetValues('weekend')
    const f = dayjs(from)
    const u = dayjs(until)
    expect(f.day()).toBe(5) // Thứ Sáu
    expect(f.hour()).toBe(17)
    expect(u.day()).toBe(0) // Chủ nhật
    expect(u.hour()).toBe(21)
    // Điểm mở phải ở tương lai (hoặc T6 17:00 hôm nay).
    expect(u.isAfter(f)).toBe(true)
  })

  it('7days: bắt đầu ngay, kéo dài đúng 7 ngày', () => {
    const { from, until } = schedulePresetValues('7days')
    const f = dayjs(from)
    const u = dayjs(until)
    expect(u.diff(f, 'day')).toBe(7)
    expect(u.hour()).toBe(f.hour())
  })

  it('custom: để trống cả hai mốc', () => {
    expect(schedulePresetValues('custom')).toEqual({ from: '', until: '' })
  })
})
