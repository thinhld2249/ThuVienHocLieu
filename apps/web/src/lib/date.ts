import dayjs from "dayjs";
import "dayjs/locale/vi";
import timezone from "dayjs/plugin/timezone";
import utc from "dayjs/plugin/utc";

dayjs.extend(utc);
dayjs.extend(timezone);
dayjs.locale("vi");

export const TZ = "Asia/Ho_Chi_Minh";

// dayjs.utc(iso).tz(TZ): chuyển MỘT KHẮC UTC sang giờ VN.
// (dayjs.tz(iso, TZ) lại HIỂU input là giờ tường thời ở TZ đó — không phải convert.)
/** ISO (UTC) → hiển thị giờ VN dạng dd/MM/yyyy HH:mm (spec §8.5). */
export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  return dayjs.utc(iso).tz(TZ).format("DD/MM/YYYY HH:mm");
}

export function formatTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  return dayjs.utc(iso).tz(TZ).format("HH:mm");
}

export function formatDate(iso: string | Date | null | undefined): string {
  if (!iso) return "—";
  return dayjs.utc(iso).tz(TZ).format("DD/MM/YYYY");
}

/** Điểm thang 10 hiển thị dấu phẩy (spec §13: 8,75). */
export function formatScore10(score: number | null | undefined): string {
  if (score == null) return "—";
  return score.toLocaleString("vi-VN", { maximumFractionDigits: 2 });
}

/** ISO UTC → "YYYY-MM-DDTHH:mm" theo giờ VN cho input datetime-local. */
export function toLocalInput(iso: string | null | undefined): string {
  if (!iso) return "";
  return dayjs.utc(iso).tz(TZ).format("YYYY-MM-DDTHH:mm");
}

/**
 * Giá trị input datetime-local (giờ VN) → ISO UTC để gửi API.
 * dayjs.tz(value, TZ) hiểu input là giờ tường thời ở VN → convert đúng.
 */
export function localInputToUtcIso(value: string): string | null {
  if (!value) return null;
  const d = dayjs.tz(value, TZ);
  return d.isValid() ? d.toISOString() : null;
}
