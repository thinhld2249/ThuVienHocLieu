import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { ChevronRight, Clock3, Download, ListChecks, Play } from "lucide-react";
import { usePublicQuiz } from "@/features/attempts/api";
import { addLocalAttempt } from "@/features/attempts/api";
import { useCreateAttempt } from "@/features/attempts/api";
import { contentUrl } from "@/lib/slug";
import { formatDateTime } from "@/lib/date";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/common/EmptyState";

/** Nhãn trạng thái mở/đóng cho khách (spec §4.4/§6.6). */
function openLabel(
  state: string,
  from: string | null,
  until: string | null,
): string {
  switch (state) {
    case "Visible":
      return until ? `Đang mở đến ${formatDateTime(until)}` : "Đang mở";
    case "ScheduledUpcoming":
      return `Sắp mở lúc ${from ? formatDateTime(from) : "—"}`;
    case "ScheduledOpen":
      return until ? `Đang mở đến ${formatDateTime(until)}` : "Đang mở";
    case "Closed":
      return "Đã đóng";
    default:
      return state;
  }
}

/** /bai-tap/:slugId (URL /{slug}-{id}) — giới thiệu quiz + nhận diện + bắt đầu (spec §5.1, §6.6). */
export function BaiTapPage() {
  const { slugId: routeSlug = "" } = useParams<{ slugId: string }>();
  const match = /^(.*)-(\d+)$/.exec(routeSlug);
  const id = match ? Number(match[2]) : NaN;
  const navigate = useNavigate();

  const { data, isPending, isError } = usePublicQuiz(
    Number.isFinite(id) ? id : null,
  );
  const createAttempt = useCreateAttempt(data?.id ?? null);

  const [guestName, setGuestName] = useState("");
  const [guestClass, setGuestClass] = useState("");
  const [formError, setFormError] = useState<string | null>(null);

  // Slug trong URL sai → về slug đúng (spec §8.4).
  useEffect(() => {
    if (data && match && data.slug !== match[1])
      navigate(contentUrl("bai-tap", data.slug, data.id), { replace: true });
  }, [data, match, navigate]);

  if (isPending)
    return (
      <div className="o-li">
        <div className="mx-auto w-full max-w-2xl px-4 py-10">
          <Skeleton className="mb-4 h-5 w-1/2" />
          <Skeleton className="mb-3 h-8 w-3/4" />
          <Skeleton className="mb-8 h-32 rounded-card" />
          <Skeleton className="h-12 rounded-card" />
        </div>
      </div>
    );

  if (isError || data == null)
    return (
      <div className="o-li">
        <div className="mx-auto w-full max-w-2xl px-4 py-10">
          <EmptyState
            title="Không tìm thấy bài tập"
            description="Bài tập không tồn tại, đã bị gỡ, hoặc chưa được mở."
            action={
              <Link to="/" className="text-sm text-violet hover:underline">
                Về trang chủ
              </Link>
            }
          />
        </div>
      </div>
    );

  const isOpen =
    data.publishState === "Visible" || data.publishState === "ScheduledOpen";
  const needName =
    data.identityMode === "Name" || data.identityMode === "NameAndClass";
  const needClass = data.identityMode === "NameAndClass";

  const start = async () => {
    setFormError(null);
    if (needName && guestName.trim().length === 0) {
      setFormError("Vui lòng nhập họ tên.");
      return;
    }
    try {
      const attempt = await createAttempt.mutateAsync({
        guestName: needName ? guestName.trim() : undefined,
        guestClass: needClass ? guestClass.trim() : undefined,
      });
      addLocalAttempt({
        attemptId: attempt.id,
        quizId: data.id,
        quizSlug: data.slug,
        quizTitle: data.title,
      });
      navigate(`/lam-bai/${attempt.id}`);
    } catch (e) {
      setFormError(
        e instanceof Error ? e.message : "Không bắt đầu được bài làm",
      );
    }
  };

  return (
    <div className="o-li">
      <div className="mx-auto w-full max-w-2xl px-4 py-8">
        {/* Breadcrumb */}
        <nav
          aria-label="Breadcrumb"
          className="mb-3 flex items-center gap-1 text-sm text-muted"
        >
          <Link to="/" className="hover:text-violet">
            Trang chủ
          </Link>
          <ChevronRight className="size-3.5" aria-hidden />
          <span className="line-clamp-1 text-ink">{data.title}</span>
        </nav>

        <h1 className="mb-3 text-2xl font-semibold leading-snug">
          {data.title}
        </h1>

        {/* Nhãn */}
        <div className="mb-4 flex flex-wrap gap-1.5">
          {data.sectionName ? <Badge>{data.sectionName}</Badge> : null}
          {data.grade != null ? (
            <Badge variant="outline">Khối {data.grade}</Badge>
          ) : null}
          {data.subjectName ? (
            <Badge variant="outline">{data.subjectName}</Badge>
          ) : null}
          {data.weekNo != null ? (
            <Badge variant="outline">Tuần {data.weekNo}</Badge>
          ) : null}
          {data.schoolYearName ? (
            <Badge variant="outline">{data.schoolYearName}</Badge>
          ) : null}
        </div>

        {/* Trạng thái + thông số */}
        <div
          className={`mb-4 rounded-card border p-3 text-sm ${
            isOpen
              ? "border-correct/40 bg-correct/10 text-correct"
              : "border-grid bg-white text-muted"
          }`}
          role="status"
        >
          {openLabel(data.publishState, data.publishFrom, data.publishUntil)}
        </div>

        <ul className="mb-5 space-y-1.5 text-sm text-ink">
          <li className="flex items-center gap-2">
            <ListChecks className="size-4 text-muted" aria-hidden />
            {data.questionCount} câu
          </li>
          <li className="flex items-center gap-2">
            <Clock3 className="size-4 text-muted" aria-hidden />
            {data.timeLimitMinutes
              ? `Thời gian: ${data.timeLimitMinutes} phút`
              : "Không giới hạn thời gian"}
          </li>
          {data.maxAttempts ? (
            <li className="flex items-center gap-2">
              <Play className="size-4 text-muted" aria-hidden />
              Tối đa {data.maxAttempts} lượt làm
            </li>
          ) : null}
        </ul>

        {data.descriptionHtml ? (
          <div
            className="mb-6 max-w-prose text-base leading-relaxed [&_iframe]:my-3 [&_iframe]:aspect-video [&_iframe]:h-auto [&_iframe]:w-full [&_iframe]:rounded-card [&_iframe]:border [&_iframe]:border-grid [&_img]:max-w-full [&_p]:my-1"
            dangerouslySetInnerHTML={{ __html: data.descriptionHtml }}
          />
        ) : null}

        {data.printFileId ? (
          <a
            href={`/api/files/${data.printFileId}/download`}
            className="mb-6 inline-flex h-9 items-center gap-1.5 rounded-btn border border-grid bg-white px-3 text-sm text-ink transition hover:border-violet/40 hover:text-violet"
          >
            <Download className="size-4" aria-hidden /> Đề in (file kèm)
          </a>
        ) : null}

        {/* Nhận diện + bắt đầu */}
        <div className="rounded-card border border-grid bg-white p-4">
          {data.inProgressAttemptId ? (
            <>
              <p className="mb-3 text-sm text-ink">
                Bạn có một lượt làm bài chưa nộp trên thiết bị này.
              </p>
              <div className="flex flex-wrap gap-2">
                <Button
                  onClick={() =>
                    navigate(`/lam-bai/${data.inProgressAttemptId}`)
                  }
                >
                  <Play className="size-4" aria-hidden /> Làm tiếp
                </Button>
                {isOpen && data.maxAttempts == null ? (
                  <Button variant="outline" onClick={() => void start()}>
                    Bắt đầu lượt mới
                  </Button>
                ) : null}
              </div>
            </>
          ) : isOpen ? (
            <form
              onSubmit={(e) => {
                e.preventDefault();
                void start();
              }}
              className="space-y-3"
            >
              {needName ? (
                <div>
                  <label
                    htmlFor="guest-name"
                    className="mb-1 block text-sm font-medium"
                  >
                    Họ và tên
                  </label>
                  <input
                    id="guest-name"
                    type="text"
                    value={guestName}
                    onChange={(e) => setGuestName(e.target.value)}
                    maxLength={100}
                    placeholder="VD: Nguyễn Văn An"
                    className="h-11 w-full rounded-btn border border-grid px-3 text-base outline-none focus:border-violet"
                  />
                </div>
              ) : null}
              {needClass ? (
                <div>
                  <label
                    htmlFor="guest-class"
                    className="mb-1 block text-sm font-medium"
                  >
                    Lớp (tùy chọn)
                  </label>
                  <input
                    id="guest-class"
                    type="text"
                    value={guestClass}
                    onChange={(e) => setGuestClass(e.target.value)}
                    maxLength={60}
                    placeholder="VD: 5A"
                    className="h-11 w-full rounded-btn border border-grid px-3 text-base outline-none focus:border-violet"
                  />
                </div>
              ) : null}
              {formError ? (
                <p className="text-sm text-redpen" role="alert">
                  {formError}
                </p>
              ) : null}
              <Button
                type="submit"
                size="lg"
                className="w-full sm:w-auto"
                disabled={createAttempt.isPending}
              >
                <Play className="size-4" aria-hidden />
                {createAttempt.isPending ? "Đang mở bài…" : "Bắt đầu làm bài"}
              </Button>
            </form>
          ) : (
            <p className="text-sm text-muted">
              Bài tập hiện không mở. Bạn có thể quay lại sau.
            </p>
          )}
        </div>
      </div>
    </div>
  );
}
