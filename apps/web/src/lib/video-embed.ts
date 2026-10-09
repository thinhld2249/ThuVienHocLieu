/**
 * Link video → URL nhúng iframe (thay thế upload file video, decisions.md 2026-10-09).
 * Hỗ trợ: YouTube (watch / youtu.be / shorts), Vimeo, file video trên Google Drive.
 * BE chỉ giữ iframe khi host thuộc danh sách này (HtmlSanitize).
 */
export function videoEmbedUrl(raw: string): string | null {
  let u: URL;
  try {
    u = new URL(raw.trim());
  } catch {
    return null;
  }
  if (u.protocol !== "https:") return null;
  const host = u.hostname.replace(/^www\./, "").toLowerCase();

  // YouTube: /watch?v=ID, youtu.be/ID, /shorts/ID → youtube-nocookie embed
  const ytId =
    host === "youtube.com"
      ? u.pathname === "/watch"
        ? u.searchParams.get("v")
        : u.pathname.match(/^\/shorts\/([\w-]{4,})$/)?.[1]
      : host === "youtu.be"
        ? u.pathname.match(/^\/([\w-]{4,})$/)?.[1]
        : null;
  if (ytId) return `https://www.youtube-nocookie.com/embed/${ytId}`;

  if (host === "youtube-nocookie.com") {
    return u.pathname.match(/^\/embed\/[\w-]{4,}$/) ? u.toString() : null;
  }

  // Vimeo: vimeo.com/123456 hoặc player.vimeo.com/video/123456
  const vimeoId =
    host === "vimeo.com"
      ? u.pathname.match(/^\/(\d+)$/)?.[1]
      : host === "player.vimeo.com"
        ? u.pathname.match(/^\/video\/(\d+)$/)?.[1]
        : null;
  if (vimeoId) return `https://player.vimeo.com/video/${vimeoId}`;

  // Google Drive: /file/d/FILE_ID/view → /preview (phát video/file ngay)
  if (host === "drive.google.com") {
    const driveId = u.pathname.match(/^\/file\/d\/([\w-]{10,})\//)?.[1];
    if (driveId) return `https://drive.google.com/file/d/${driveId}/preview`;
  }

  return null;
}

/** Khối iframe nhúng (kích thước tham chiếu — CSS công khai co giãn theo màn hình). */
export function videoEmbedHtml(embedUrl: string): string {
  return (
    `<iframe src="${embedUrl}" width="560" height="315" frameborder="0" ` +
    `allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture" ` +
    `allowfullscreen title="Video"></iframe>`
  );
}

/**
 * Dòng text chỉ chứa đúng 1 link video → trả HTML nhúng, khác null.
 * Link nằm lẫn trong câu văn thì không nhúng (đỡ hiểu nhầm).
 */
export function videoEmbedBlockForLine(line: string): string | null {
  const t = line.trim();
  if (!/^https:\/\S+$/.test(t)) return null;
  const embed = videoEmbedUrl(t);
  return embed ? videoEmbedHtml(embed) : null;
}
