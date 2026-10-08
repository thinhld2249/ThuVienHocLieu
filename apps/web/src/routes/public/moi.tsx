import { useState } from "react";
import { GoogleLogin } from "@react-oauth/google";
import type { CredentialResponse } from "@react-oauth/google";
import { useNavigate, useParams } from "react-router";
import { Loader2, MailCheck } from "lucide-react";
import { useInvitation, useLogin } from "@/features/auth/api";
import { isInAppBrowser } from "@/lib/inapp-browser";
import { formatDate } from "@/lib/date";

/**
 * §3.3: chấp nhận lời mời qua link /moi/{token}.
 * BE kiểm tra email khớp lời mời khi đăng nhập → Active + vào tổ.
 */
export function MoiPage() {
  const { token } = useParams<{ token: string }>();
  const navigate = useNavigate();
  const { data: invite, isPending, isError } = useInvitation(token);
  const login = useLogin();
  const [error, setError] = useState<string | null>(null);

  if (isPending)
    return (
      <Center>
        <Loader2 className="size-6 animate-spin text-violet" aria-hidden />
      </Center>
    );

  if (isError || !invite)
    return (
      <Center>
        <Card>
          <h1 className="text-xl font-semibold">Lời mời không hợp lệ</h1>
          <p className="mt-2 text-sm text-muted">
            Liên kết này không tồn tại hoặc đã hết hạn. Hãy liên hệ tổ trưởng để
            được gửi lại.
          </p>
        </Card>
      </Center>
    );

  const open = invite.status === "Chờ";

  async function onCredential(credential: string) {
    if (!token) return;
    setError(null);
    try {
      const me = await login.mutateAsync({
        idToken: credential,
        inviteToken: token,
      });
      navigate(me.status === "Pending" ? "/cho-duyet" : "/gv", {
        replace: true,
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Đăng nhập không thành công");
    }
  }

  return (
    <Center>
      <Card className="max-w-md">
        <div className="flex flex-col items-center gap-2 text-center">
          <MailCheck className="size-10 text-violet" aria-hidden />
          <h1 className="text-2xl font-semibold">Lời mời tham gia</h1>
        </div>

        <dl className="mt-5 space-y-2 rounded-btn bg-paper p-4 text-sm">
          <Row label="Tổ" value={invite.teamName} />
          <Row label="Email được mời" value={invite.emailMasked} />
          <Row label="Hết hạn" value={formatDate(invite.expiresAt)} />
          {!open && (
            <p className="rounded-btn border border-warn/40 bg-warn/10 p-2 text-xs text-ink">
              Trạng thái lời mời: {invite.status}. Liên hệ tổ trưởng để được gửi
              lại.
            </p>
          )}
        </dl>

        {open &&
          (isInAppBrowser() ? (
            <p className="mt-4 rounded-btn border border-violet/30 bg-violet/5 p-3 text-sm text-ink">
              Google không cho đăng nhập trong ứng dụng. Mở liên kết này bằng
              trình duyệt Chrome hoặc Safari.
            </p>
          ) : import.meta.env.VITE_GOOGLE_CLIENT_ID ? (
            <div className="mt-4 flex flex-col gap-3">
              <GoogleLogin
                onSuccess={(r: CredentialResponse) =>
                  r.credential
                    ? onCredential(r.credential)
                    : setError("Google không phản hồi.")
                }
                onError={() =>
                  setError(
                    "Google không phản hồi. Thử lại hoặc dùng trình duyệt khác.",
                  )
                }
                width={300}
              />
              {login.isPending && (
                <p className="flex items-center justify-center gap-2 text-sm text-muted">
                  <Loader2 className="size-4 animate-spin" aria-hidden />
                  Đang xác thực…
                </p>
              )}
            </div>
          ) : null)}

        {error && (
          <p
            role="alert"
            className="mt-4 rounded-btn border border-wrong/40 bg-wrong/10 p-3 text-sm text-wrong"
          >
            {error}
          </p>
        )}

        <p className="mt-4 text-center text-xs text-muted">
          Bạn cần đăng nhập chính xác bằng email{" "}
          <strong>{invite.emailMasked}</strong> để chấp nhận lời mời.
        </p>
      </Card>
    </Center>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-muted">{label}</dt>
      <dd className="text-right font-medium text-ink">{value}</dd>
    </div>
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
