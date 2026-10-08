/** URL nội dung dạng /{slug}-{id}; lấy slug từ URL khi cần đối chiếu. */
export function slugFromUrl(url: string): string {
  const last = url.split('/').filter(Boolean).pop() ?? ''
  const m = /^(.*)-(\d+)$/.exec(last)
  return m?.[1] ?? last
}

export function contentUrl(kind: 'tai-lieu' | 'bai-tap', slug: string, id: number): string {
  return `/${kind}/${slug}-${id}`
}
