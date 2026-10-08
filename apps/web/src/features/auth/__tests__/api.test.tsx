import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi, beforeEach } from 'vitest'
import * as apiClient from '@/lib/api-client'
import { apiErrorTitle, useMe } from '@/features/auth/api'

vi.mock('@/lib/api-client', () => ({
  api: {
    GET: vi.fn(),
    PUT: vi.fn(),
    POST: vi.fn(),
  },
}))

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>
}

describe('apiErrorTitle', () => {
  it('trích title tiếng Việt từ ProblemDetails', () => {
    expect(apiErrorTitle({ data: { title: 'Số điện thoại không hợp lệ.' } }, 'fallback')).toBe(
      'Số điện thoại không hợp lệ.',
    )
  })
  it('dùng fallback khi không có title', () => {
    expect(apiErrorTitle(undefined, 'Lỗi mạng')).toBe('Lỗi mạng')
  })
})

describe('useMe', () => {
  beforeEach(() => {
    vi.mocked(apiClient.api.GET).mockReset()
  })

  it('401 (chưa đăng nhập) → data null, không throw', async () => {
    vi.mocked(apiClient.api.GET).mockResolvedValue({
      data: undefined,
      error: undefined,
      response: { status: 401 } as Response,
    })
    const { result } = renderHook(() => useMe(), { wrapper })
    await waitFor(() => expect(result.current.isPending).toBe(false))
    expect(result.current.data).toBeNull()
    expect(result.current.error).toBeNull()
  })

  it('200 → trả Me', async () => {
    const me = {
      id: 1,
      email: 'gv@gmail.com',
      fullName: 'GV Test',
      phone: null,
      avatarUrl: null,
      systemRole: 'Teacher',
      status: 'Active',
      statusReason: null,
      teams: [],
      requestedTeamId: null,
    }
    vi.mocked(apiClient.api.GET).mockResolvedValue({
      data: me,
      error: undefined,
      response: { status: 200 } as Response,
    })
    const { result } = renderHook(() => useMe(), { wrapper })
    await waitFor(() => expect(result.current.data?.email).toBe('gv@gmail.com'))
  })
})
