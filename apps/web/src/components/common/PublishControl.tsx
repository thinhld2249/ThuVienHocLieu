import { useEffect, useRef, useState } from "react";
import dayjs from "dayjs";
import { CalendarClock, Eye, EyeOff } from "lucide-react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import type { PublishMode, PublishRequest } from "@/features/documents/types";

/** Nhãn trạng thái hiển thị (spec §4.4). */
export function publishStateLabel(state: string): string {
  switch (state) {
    case "Visible":
      return "Đang hiện";
    case "Hidden":
      return "Đang ẩn";
    case "ScheduledUpcoming":
      return "Sắp mở";
    case "ScheduledOpen":
      return "Đang mở";
    case "Closed":
      return "Đã đóng";
    default:
      return state;
  }
}

export function publishStateBadgeClass(state: string): string {
  switch (state) {
    case "Visible":
    case "ScheduledOpen":
      return "bg-correct/10 text-correct";
    case "ScheduledUpcoming":
      return "bg-warn/10 text-warn";
    case "Closed":
      return "bg-muted/10 text-muted";
    default:
      return "bg-muted/10 text-muted";
  }
}

/** Local (input datetime-local) → ISO UTC cho BE. */
function toIsoUtc(value: string): string | null {
  if (!value) return null;
  return dayjs(value).utc().toISOString();
}

/** ISO UTC → local cho input datetime-local. */
function toLocalInput(iso: string | null | undefined): string {
  if (!iso) return "";
  return dayjs(iso).format("YYYY-MM-DDTHH:mm");
}

/** Thứ Sáu 17:00 gần nhất (dayjs: 0=CN … 5=T6; nếu hôm nay T6 trước 17:00 thì hôm nay). */
function nextWeekendFrom(): dayjs.Dayjs {
  const now = dayjs();
  let from = now.day(5).hour(17).minute(0).second(0).millisecond(0);
  if (from.isBefore(now)) from = from.add(7, "day");
  return from;
}

export type SchedulePreset = "custom" | "weekend" | "7days";

/** Giá trị (local, cho input datetime-local) của preset — dùng chung UI 1 item & bulk. */
export function schedulePresetValues(p: SchedulePreset): {
  from: string;
  until: string;
} {
  if (p === "weekend") {
    const f = nextWeekendFrom();
    return {
      from: f.format("YYYY-MM-DDTHH:mm"),
      until: f.add(2, "day").hour(21).minute(0).format("YYYY-MM-DDTHH:mm"),
    };
  }
  if (p === "7days") {
    const f = dayjs().add(1, "minute");
    return {
      from: f.format("YYYY-MM-DDTHH:mm"),
      until: f.add(7, "day").format("YYYY-MM-DDTHH:mm"),
    };
  }
  return { from: "", until: "" };
}

/**
 * Hiện/Ẩn + Hẹn giờ 1 nội dung (spec §4.4).
 * Component "ngu" về API: nhận callback `publish` từ form/bảng để dùng chung
 * mutation (1 item hoặc bulk).
 */
export function PublishControl({
  publishState,
  publishFrom,
  publishUntil,
  publish,
  busy,
  className,
}: {
  publishState: string;
  publishFrom?: string | null;
  publishUntil?: string | null;
  publish: (req: PublishRequest) => Promise<void>;
  busy?: boolean;
  className?: string;
}) {
  const [open, setOpen] = useState(false);
  const [preset, setPreset] = useState<SchedulePreset>("custom");
  const [from, setFrom] = useState(() => toLocalInput(publishFrom));
  const [until, setUntil] = useState(() => toLocalInput(publishUntil));
  const [submitting, setSubmitting] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const popRef = useRef<HTMLDivElement>(null);

  // Đóng popover khi bấm ra ngoài.
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (popRef.current && !popRef.current.contains(e.target as Node))
        setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open]);

  const visibleNow =
    publishState === "Visible" || publishState === "ScheduledOpen";

  const toggleVisible = async () => {
    if (busy) return;
    await publish({ mode: visibleNow ? "Hidden" : "Visible" });
  };

  const applyPreset = (p: SchedulePreset) => {
    setPreset(p);
    if (p !== "custom") {
      const v = schedulePresetValues(p);
      setFrom(v.from);
      setUntil(v.until);
    }
  };

  const submitScheduled = async () => {
    if (submitting) return;
    setSubmitting(true);
    try {
      await publish({
        mode: "Scheduled" satisfies PublishMode,
        from: toIsoUtc(from),
        until: toIsoUtc(until),
      });
      setOpen(false);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div
      ref={rootRef}
      className={cn("relative inline-flex items-center gap-2", className)}
    >
      <Badge className={publishStateBadgeClass(publishState)}>
        {publishStateLabel(publishState)}
      </Badge>
      <Button
        variant="outline"
        size="sm"
        onClick={() => void toggleVisible()}
        disabled={busy}
      >
        {visibleNow ? (
          <>
            <EyeOff className="size-3.5" aria-hidden /> Ẩn
          </>
        ) : (
          <>
            <Eye className="size-3.5" aria-hidden /> Hiện
          </>
        )}
      </Button>
      <Button
        variant="outline"
        size="sm"
        onClick={() => setOpen((v) => !v)}
        disabled={busy}
        aria-expanded={open}
      >
        <CalendarClock className="size-3.5" aria-hidden /> Hẹn giờ
      </Button>

      {open ? (
        <div
          ref={popRef}
          role="dialog"
          aria-label="Hẹn giờ hiển thị"
          className="absolute left-0 top-full z-30 mt-2 w-72 rounded-card border border-grid bg-white p-3 shadow-lg"
        >
          <div
            className="mb-2 flex gap-1"
            role="group"
            aria-label="Preset thời gian"
          >
            {(
              [
                ["weekend", "Cuối tuần này"],
                ["7days", "7 ngày"],
                ["custom", "Tùy chỉnh"],
              ] as [SchedulePreset, string][]
            ).map(([p, label]) => (
              <button
                key={p}
                type="button"
                onClick={() => applyPreset(p)}
                className={cn(
                  "rounded-btn px-2 py-1 text-xs transition",
                  preset === p
                    ? "bg-violet text-white"
                    : "bg-paper text-muted hover:text-violet",
                )}
              >
                {label}
              </button>
            ))}
          </div>
          <label className="mb-1 block text-xs text-muted">Từ</label>
          <input
            type="datetime-local"
            value={from}
            onChange={(e) => {
              setFrom(e.target.value);
              setPreset("custom");
            }}
            className="mb-2 h-9 w-full rounded-btn border border-grid px-2 text-sm outline-none focus:border-violet"
          />
          <label className="mb-1 block text-xs text-muted">
            Đến (trống = không giới hạn)
          </label>
          <input
            type="datetime-local"
            value={until}
            onChange={(e) => {
              setUntil(e.target.value);
              setPreset("custom");
            }}
            className="mb-3 h-9 w-full rounded-btn border border-grid px-2 text-sm outline-none focus:border-violet"
          />
          <Button
            size="sm"
            className="w-full"
            disabled={submitting || !from}
            onClick={() => void submitScheduled()}
          >
            {submitting ? "Đang lưu…" : "Lên lịch"}
          </Button>
          <p className="mt-1.5 text-[11px] leading-snug text-muted">
            Cuối tuần này = T6 17:00 → CN 21:00 (giờ VN)
          </p>
        </div>
      ) : null}
    </div>
  );
}
