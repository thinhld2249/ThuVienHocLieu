import { useEffect, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { Clock3, ListChecks, Play } from "lucide-react";
import { addLocalAttempt, listLocalAttempts } from "@/features/attempts/api";
import {
  useCreateAssignmentAttempt,
  usePublicAssignment,
} from "@/features/classes/api";
import { formatDateTime } from "@/lib/date";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/common/EmptyState";

/**
 * /vao-lop — nhập mã giao bài 6 ký tự (hoặc ?ma=CODE từ QR) →
 * chọn tên trong lớp / nhập tên → làm bài (spec §5.1, §7).
 */
export function VaoLopPage() {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const rawCode = (searchParams.get("ma") ?? "").trim().toUpperCase();
  const code = /^[A-Z0-9]{6}$/.test(rawCode) ? rawCode : null;

  const [input, setInput] = useState(rawCode ?? "");
  const [localAttempts] = useState(() => listLocalAttempts());

  // Ma trong URL đổi (quét QR lần 2) → cập nhật ô nhập.
  useEffect(() => {
    setInput(rawCode ?? "");
  }, [rawCode]);

  const submit = () => {
    const c = input.trim().toUpperCase();
    if (/^[A-Z0-9]{6}$/.test(c)) setSearchParams({ ma: c });
  };

  if (!code)
    return (
      <div className="o-li">
        <div className="mx-auto w-full max-w-md px-4 py-10">
          <h1 className="mb-2 text-2xl font-semibold">Vào lớp làm bài</h1>
          <p className="mb-6 text-sm text-muted">
            Nhập mã 6 ký tự mà giáo viên gửi (hoặc quét mã QR) để bắt đầu làm
            bài.
          </p>
          <form
            className="flex gap-2"
            onSubmit={(e) => {
              e.preventDefault();
              submit();
            }}
          >
            <input
              type="text"
              aria-label="Mã giao bài"
              value={input}
              onChange={(e) =>
                setInput(e.target.value.toUpperCase().slice(0, 6))
              }
              placeholder="ABC123"
              autoCapitalize="characters"
              autoCorrect="off"
              className="h-12 w-full rounded-btn border border-grid bg-white px-3 text-center font-mono text-xl tracking-[0.3em] outline-none focus:border-violet"
            />
            <Button type="submit" size="lg" disabled={input.length !== 6}>
              Vào
            </Button>
          </form>

          {localAttempts.length > 0 ? (
            <div className="mt-8 rounded-card border border-grid bg-white p-4">
              <h2 className="mb-2 text-sm font-semibold">
                Bài đang làm dở trên thiết bị này
              </h2>
              <ul className="space-y-2">
                {localAttempts.map((a) => (
                  <li key={a.attemptId} className="flex items-center gap-2">
                    <span className="min-w-0 flex-1 truncate text-sm">
                      {a.quizTitle}
                    </span>
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => navigate(`/lam-bai/${a.attemptId}`)}
                    >
                      <Play className="size-3.5" aria-hidden /> Làm tiếp
                    </Button>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
        </div>
      </div>
    );

  return (
    <AssignmentGate
      code={code}
      onBack={() => {
        setSearchParams({});
        setInput("");
      }}
    />
  );
}

function AssignmentGate({
  code,
  onBack,
}: {
  code: string;
  onBack: () => void;
}) {
  const navigate = useNavigate();
  const { data, isPending, isError } = usePublicAssignment(code);
  const createAttempt = useCreateAssignmentAttempt(code);

  const [studentId, setStudentId] = useState<number | null>(null);
  const [guestName, setGuestName] = useState("");
  const [error, setError] = useState<string | null>(null);

  if (isPending)
    return (
      <div className="o-li">
        <div className="mx-auto w-full max-w-2xl px-4 py-10">
          <Skeleton className="mb-4 h-5 w-1/3" />
          <Skeleton className="mb-3 h-8 w-3/4" />
          <Skeleton className="mb-8 h-40 rounded-card" />
        </div>
      </div>
    );

  if (isError || data == null)
    return (
      <div className="o-li">
        <div className="mx-auto w-full max-w-2xl px-4 py-10">
          <EmptyState
            title="Không tìm thấy bài tập"
            description="Mã không tồn tại hoặc đã bị thu hồi. Kiểm tra lại mã 6 ký tự giáo viên gửi."
            action={
              <Button variant="outline" size="sm" onClick={onBack}>
                Nhập mã khác
              </Button>
            }
          />
        </div>
      </div>
    );

  const roster = data.roster;
  const needName =
    roster == null &&
    (data.identityMode === "Name" || data.identityMode === "NameAndClass");
  const limitReached =
    data.maxAttempts != null && data.usedAttempts >= data.maxAttempts;

  const start = async () => {
    setError(null);
    if (roster != null && studentId == null) {
      setError("Chọn tên bạn trong danh sách lớp.");
      return;
    }
    if (needName && guestName.trim().length === 0) {
      setError("Vui lòng nhập họ tên.");
      return;
    }
    try {
      const attempt = await createAttempt.mutateAsync({
        studentId: roster != null ? studentId : null,
        guestName: roster != null ? undefined : guestName.trim() || undefined,
      });
      addLocalAttempt({
        attemptId: attempt.id,
        quizId: 0,
        quizSlug: "",
        quizTitle: data.quizTitle,
      });
      navigate(`/lam-bai/${attempt.id}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không bắt đầu được bài làm");
    }
  };

  return (
    <div className="o-li">
      <div className="mx-auto w-full max-w-2xl px-4 py-8">
        <nav
          aria-label="Breadcrumb"
          className="mb-3 flex items-center gap-1 text-sm text-muted"
        >
          <button type="button" onClick={onBack} className="hover:text-violet">
            Vào lớp
          </button>
          <span aria-hidden>›</span>
          <span className="font-mono tracking-widest">{code}</span>
        </nav>

        <h1 className="mb-1 text-2xl font-semibold leading-snug">
          {data.quizTitle}
        </h1>
        <p className="mb-4 text-sm text-muted">
          Lớp {data.className} · {data.questionCount} câu
          {data.timeLimitMinutes ? ` · ${data.timeLimitMinutes} phút` : ""}
        </p>

        {data.status === "Scheduled" ? (
          <div
            role="status"
            className="mb-6 flex items-center gap-2 rounded-card border border-warn/40 bg-warn/10 p-3 text-sm text-warn"
          >
            <Clock3 className="size-4" aria-hidden />
            Bài chưa mở — sẽ mở lúc {formatDateTime(data.openAt)}. Quay lại sau
            giờ mở.
          </div>
        ) : null}

        {data.status === "Closed" ? (
          <div
            role="status"
            className="mb-6 rounded-card border border-grid bg-white p-3 text-sm text-muted"
          >
            Bài đã đóng
            {data.closeAt ? ` lúc ${formatDateTime(data.closeAt)}` : ""}. Bạn
            không thể làm bài mới với mã này.
          </div>
        ) : null}

        {data.status === "Open" ? (
          <div className="space-y-4">
            <ul className="mb-2 space-y-1.5 text-sm text-ink">
              <li className="flex items-center gap-2">
                <ListChecks className="size-4 text-muted" aria-hidden />
                {data.questionCount} câu
                {data.timeLimitMinutes
                  ? ` · thời gian ${data.timeLimitMinutes} phút`
                  : " · không giới hạn thời gian"}
              </li>
              {data.maxAttempts != null ? (
                <li className="flex items-center gap-2">
                  <Play className="size-4 text-muted" aria-hidden />
                  Tối đa {data.maxAttempts} lượt
                  {data.usedAttempts > 0
                    ? ` — thiết bị này đã dùng ${data.usedAttempts}`
                    : ""}
                </li>
              ) : null}
            </ul>

            <div className="rounded-card border border-grid bg-white p-4">
              {limitReached ? (
                <p className="text-sm text-warn">
                  Bạn đã dùng hết lượt làm với mã này.
                </p>
              ) : roster != null ? (
                <form
                  onSubmit={(e) => {
                    e.preventDefault();
                    void start();
                  }}
                >
                  <p className="mb-3 text-sm font-medium">
                    Chọn tên bạn trong lớp {data.className}:
                  </p>
                  <div
                    className="mb-4 grid max-h-72 gap-1.5 overflow-y-auto pr-1"
                    role="radiogroup"
                    aria-label="Danh sách học sinh"
                  >
                    {roster.map((s) => (
                      <label
                        key={s.id}
                        className={`flex min-h-11 cursor-pointer items-center gap-2 rounded-btn border px-3 text-base transition ${
                          studentId === s.id
                            ? "border-violet bg-violet/10 font-medium"
                            : "border-grid hover:border-violet/40"
                        }`}
                      >
                        <input
                          type="radio"
                          name="student"
                          value={s.id}
                          checked={studentId === s.id}
                          onChange={() => setStudentId(s.id)}
                          className="size-4 accent-violet"
                        />
                        {s.fullName}
                      </label>
                    ))}
                  </div>
                  {error ? (
                    <p className="mb-3 text-sm text-redpen" role="alert">
                      {error}
                    </p>
                  ) : null}
                  <Button
                    type="submit"
                    size="lg"
                    className="w-full sm:w-auto"
                    disabled={createAttempt.isPending}
                  >
                    <Play className="size-4" aria-hidden />
                    {createAttempt.isPending
                      ? "Đang mở bài…"
                      : "Bắt đầu làm bài"}
                  </Button>
                </form>
              ) : (
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
                  {error ? (
                    <p className="text-sm text-redpen" role="alert">
                      {error}
                    </p>
                  ) : null}
                  <Button
                    type="submit"
                    size="lg"
                    className="w-full sm:w-auto"
                    disabled={createAttempt.isPending}
                  >
                    <Play className="size-4" aria-hidden />
                    {createAttempt.isPending
                      ? "Đang mở bài…"
                      : "Bắt đầu làm bài"}
                  </Button>
                </form>
              )}
            </div>
          </div>
        ) : null}

        <p className="mt-6 text-xs text-muted">
          <Link to="/" className="text-violet hover:underline">
            Về trang chủ
          </Link>
        </p>
      </div>
    </div>
  );
}
