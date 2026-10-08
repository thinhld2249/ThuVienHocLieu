import { useState } from "react";
import { Navigate, useNavigate } from "react-router";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { Check, Loader2 } from "lucide-react";
import {
  apiErrorTitle,
  useMe,
  usePublicTeams,
  useUpdateMe,
} from "@/features/auth/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

const schema = z.object({
  fullName: z.string().trim().min(2, "Họ tên phải từ 2 đến 200 ký tự").max(200),
  phone: z
    .string()
    .trim()
    .regex(/^\+?\d{6,15}$/, "Số điện thoại không hợp lệ")
    .optional()
    .or(z.literal("")),
  requestedTeamId: z.coerce.number().int("Chọn một tổ").positive("Chọn một tổ"),
});

type FormValues = z.infer<typeof schema>;

/**
 * §3.1: tài khoản Pending — chọn tổ muốn tham gia, nhập họ tên đầy đủ & SĐT
 * (tùy chọn) → PUT /api/me → vào hàng chờ của Tổ trưởng/Tổ phó tổ đó + Admin.
 */
export function ChoDuyetPage() {
  const navigate = useNavigate();
  const { data: me, isPending: meLoading } = useMe();
  const {
    data: teams,
    isPending: teamsLoading,
    isError: teamsError,
  } = usePublicTeams();
  const updateMe = useUpdateMe();
  const [error, setError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      fullName: me?.fullName ?? "",
      phone: me?.phone ?? "",
      requestedTeamId: me?.requestedTeamId ?? 0,
    },
  });

  if (meLoading)
    return (
      <Center>
        <Loader2 className="size-6 animate-spin text-violet" aria-hidden />
      </Center>
    );
  if (!me) return <Navigate to="/dang-nhap?from=%2Fcho-duyet" replace />;
  if (me.status === "Active" || me.systemRole === "Admin")
    return <Navigate to="/gv" replace />;

  // Rejected / Suspended: không cho sửa, chỉ hiện lý do (Admin xử lý)
  if (me.status !== "Pending")
    return (
      <Center>
        <Card>
          <h1 className="text-xl font-semibold">Tài khoản của bạn</h1>
          <p className="mt-2 text-sm text-ink">
            Trạng thái: <strong>{statusLabel(me.status)}</strong>
          </p>
          {me.statusReason && (
            <p className="mt-2 rounded-btn bg-paper p-3 text-sm text-muted">
              {me.statusReason}
            </p>
          )}
          <p className="mt-3 text-sm text-muted">
            Liên hệ quản trị viên trường nếu bạn cho rằng đây là nhầm lẫn.
          </p>
        </Card>
      </Center>
    );

  function onSubmit(values: FormValues) {
    setError(null);
    updateMe.mutate(
      {
        fullName: values.fullName,
        phone: values.phone || undefined,
        requestedTeamId: values.requestedTeamId,
      },
      {
        onSuccess: () => navigate("/cho-duyet?da-gui=1", { replace: true }),
        onError: (e) => setError(apiErrorTitle(e, "Không gửi được yêu cầu")),
      },
    );
  }

  const alreadySent =
    new URLSearchParams(window.location.search).get("da-gui") === "1";

  return (
    <Center>
      <Card className="max-w-lg">
        {alreadySent ? (
          <div className="text-center">
            <Check className="mx-auto mb-3 size-10 text-correct" aria-hidden />
            <h1 className="text-xl font-semibold">Đã gửi yêu cầu</h1>
            <p className="mt-2 text-sm text-muted">
              Tổ trưởng hoặc tổ phó của tổ bạn chọn sẽ xem xét. Khi được duyệt,
              bạn đăng nhập lại vào trang này để vào không gian tổ.
            </p>
          </div>
        ) : (
          <>
            <h1 className="text-xl font-semibold">
              Xin tham gia tổ chuyên môn
            </h1>
            <p className="mt-1 text-sm text-muted">
              Điền thông tin và chọn tổ. Tổ trưởng/tổ phó (hoặc quản trị viên)
              sẽ xem xét.
            </p>

            <form
              className="mt-5 space-y-4"
              onSubmit={handleSubmit(onSubmit)}
              noValidate
            >
              <Field label="Họ và tên" error={errors.fullName?.message}>
                <Input
                  {...register("fullName")}
                  placeholder="Nguyễn Văn A"
                  autoComplete="name"
                />
              </Field>

              <Field
                label="Số điện thoại (tùy chọn)"
                error={errors.phone?.message}
              >
                <Input
                  {...register("phone")}
                  placeholder="0912 345 678"
                  inputMode="tel"
                  autoComplete="tel"
                />
              </Field>

              <Field
                label="Tổ chuyên môn"
                error={errors.requestedTeamId?.message}
              >
                {teamsLoading ? (
                  <p className="flex h-10 items-center gap-2 text-sm text-muted">
                    <Loader2 className="size-4 animate-spin" aria-hidden /> Đang
                    tải…
                  </p>
                ) : teamsError || !teams || teams.length === 0 ? (
                  <p className="text-sm text-muted">
                    Chưa có tổ nào hoạt động. Liên hệ quản trị viên để được mời.
                  </p>
                ) : (
                  <select
                    {...register("requestedTeamId")}
                    className="h-10 w-full rounded-btn border border-grid bg-white px-3 text-sm outline-none transition focus:border-violet focus:ring-2 focus:ring-violet/20"
                  >
                    <option value={0}>— Chọn tổ —</option>
                    {teams.map((t) => (
                      <option key={t.id} value={t.id}>
                        {t.name}
                        {t.gradeName ? ` — ${t.gradeName}` : ""}
                      </option>
                    ))}
                  </select>
                )}
              </Field>

              {error && (
                <p
                  role="alert"
                  className="rounded-btn border border-wrong/40 bg-wrong/10 p-3 text-sm text-wrong"
                >
                  {error}
                </p>
              )}

              <Button
                type="submit"
                className="w-full"
                disabled={updateMe.isPending}
              >
                {updateMe.isPending ? (
                  <>
                    <Loader2 className="size-4 animate-spin" aria-hidden /> Đang
                    gửi…
                  </>
                ) : (
                  "Gửi yêu cầu"
                )}
              </Button>
            </form>
          </>
        )}
      </Card>
    </Center>
  );
}

function statusLabel(status: string): string {
  return (
    {
      Active: "Đang hoạt động",
      Pending: "Đang chờ duyệt",
      Suspended: "Bị khóa",
      Rejected: "Bị từ chối",
    }[status] ?? status
  );
}

function Center({ children }: { children: React.ReactNode }) {
  return (
    <div className="o-li flex flex-1 items-start justify-center p-4 pt-14">
      {children}
    </div>
  );
}

function Card({
  children,
  className = "",
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div
      className={`w-full rounded-card border border-grid bg-white p-6 ${className}`}
    >
      {children}
    </div>
  );
}

function Field({
  label,
  error,
  children,
}: {
  label: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <label className="block">
      <span className="mb-1 block text-sm font-medium text-ink">{label}</span>
      {children}
      {error && <span className="mt-1 block text-xs text-wrong">{error}</span>}
    </label>
  );
}
