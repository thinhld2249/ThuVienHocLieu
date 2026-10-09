import { describe, expect, it } from 'vitest'
import {
  videoEmbedUrl,
  videoEmbedHtml,
  videoEmbedBlockForLine,
} from '../video-embed'

describe('videoEmbedUrl', () => {
  it('YouTube watch', () => {
    expect(videoEmbedUrl('https://www.youtube.com/watch?v=dQw4w9WgXcQ')).toBe(
      'https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ',
    )
  })

  it('YouTube shorts', () => {
    expect(videoEmbedUrl('https://www.youtube.com/shorts/AbC123xyz-_')).toBe(
      'https://www.youtube-nocookie.com/embed/AbC123xyz-_',
    )
  })

  it('youtu.be', () => {
    expect(videoEmbedUrl('https://youtu.be/AbC123xyz-_')).toBe(
      'https://www.youtube-nocookie.com/embed/AbC123xyz-_',
    )
  })

  it('Vimeo', () => {
    expect(videoEmbedUrl('https://vimeo.com/76979871')).toBe(
      'https://player.vimeo.com/video/76979871',
    )
  })

  it('Google Drive file', () => {
    expect(
      videoEmbedUrl('https://drive.google.com/file/d/1AbCdEfGhIjKlMnOp/view?usp=sharing'),
    ).toBe('https://drive.google.com/file/d/1AbCdEfGhIjKlMnOp/preview')
  })

  it('http (không https) → null', () => {
    expect(videoEmbedUrl('http://www.youtube.com/watch?v=AbC123xyz-_')).toBeNull()
  })

  it('link không phải video → null', () => {
    expect(videoEmbedUrl('https://example.com/video/1')).toBeNull()
    expect(videoEmbedUrl('https://www.google.com/search?q=toan')).toBeNull()
  })

  it('rác → null', () => {
    expect(videoEmbedUrl('https://')).toBeNull()
    expect(videoEmbedUrl('https://youtu.be/abc')).toBeNull() // id quá ngắn
    expect(videoEmbedUrl('chưa phải url')).toBeNull()
  })
})

describe('videoEmbedHtml', () => {
  it('ra iframe với src + allowfullscreen', () => {
    const html = videoEmbedHtml('https://www.youtube-nocookie.com/embed/AbC123xyz-_')
    expect(html).toContain('src="https://www.youtube-nocookie.com/embed/AbC123xyz-_"')
    expect(html).toContain('allowfullscreen')
    expect(html).toContain('<iframe')
  })
})

describe('videoEmbedBlockForLine', () => {
  it('dòng chỉ chứa 1 link video → khối nhúng', () => {
    const block = videoEmbedBlockForLine('  https://youtu.be/AbC123xyz-_  ')
    expect(block).not.toBeNull()
    expect(block).toContain('<iframe')
  })

  it('link lẫn trong câu văn → không nhúng', () => {
    expect(
      videoEmbedBlockForLine('Xem thêm tại https://youtu.be/AbC123xyz-_ nhé'),
    ).toBeNull()
  })

  it('dòng không phải link → null', () => {
    expect(videoEmbedBlockForLine('Đây là mô tả bài giảng.')).toBeNull()
    expect(videoEmbedBlockForLine('')).toBeNull()
  })
})
