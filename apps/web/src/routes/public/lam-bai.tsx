import { useEffect } from "react";
import { useNavigate, useParams } from "react-router";
import { useAttempt } from "@/features/attempts/api";
import { AttemptRunner } from "@/features/quizzes/components/attempt-runner";
import { Skeleton } from "@/components/ui/skeleton";

/** /lam-bai/:attemptId — làm bài (spec §6.6). Khóa truy cập = uuid của attempt. */
export function LamBaiPage() {
  const { attemptId } = useParams<{ attemptId: string }>();
  const navigate = useNavigate();
  const { data, isPending } = useAttempt(attemptId ?? null);

  // Đã nộp / hết giờ → chuyển trang kết quả.
  useEffect(() => {
    if (data && data.status !== "InProgress")
      navigate(`/ket-qua/${data.id}`, { replace: true });
  }, [data, navigate]);

  if (isPending || !data)
    return (
      <div className="o-li min-h-dvh">
        <div className="mx-auto w-full max-w-3xl px-4 pt-6">
          <Skeleton className="mb-4 h-10" />
          <Skeleton className="mb-3 h-9" />
          <Skeleton className="h-64 rounded-card" />
        </div>
      </div>
    );

  return (
    <AttemptRunner
      attemptId={data.id}
      title={data.title}
      data={{
        questions: data.questions,
        groups: data.groups,
        answers: data.answers,
      }}
      expiresAt={data.expiresAt}
      onFinished={(id) => navigate(`/ket-qua/${id}`)}
      onGone={(id) => navigate(`/ket-qua/${id}`)}
    />
  );
}
