import { useState, type FormEvent } from "react";
import { GoogleLogin } from "@react-oauth/google";
import type { CredentialResponse } from "@react-oauth/google";
import { Link, useNavigate, useSearchParams } from "react-router";
import { Check, Copy, Loader2 } from "lucide-react";
import { useLogin, useLoginPassword, useMe } from "@/features/auth/api";
import { isInAppBrowser } from "@/lib/inapp-browser";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

/**
 * §3.1: đăng nhập Google. Sau đăng nhập:
 * Active/Admin → `from` (mặc định /gv) · Pending → /cho-duyet.
 * Webview in-app (Zalo/FB): Google chặn đăng nhập → hướng dẫn mở bằng Chrome/Safari.
 */
export function DangNhapPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const from = params.get("from") ?? "";
  const safeFrom =
    from.startsWith("/") && !from.startsWith("//") ? from : "/gv";

  const { data: me } = useMe();
  const login = useLogin();
  const loginPassword = useLoginPassword();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  // Đã đăng nhập → về đúng chỗ (không cần bấm lại nút Google)
  if (me) {
    const dest = me.status === "Pending" ? "/cho-duyet" : safeFrom;
    return <RedirectHint dest={dest} />;
  }

  async function onCredential(credential: string) {
    setError(null);
    try {
      const me = await login.mutateAsync({ idToken: credential });
      navigate(me.status === "Pending" ? "/cho-duyet" : safeFrom, {
        replace: true,
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Đăng nhập không thành công");
    }
  }

  async function submitPassword(e: FormEvent) {
    e.preventDefault();
    if (!email.trim() || !password) return;
    setError(null);
    try {
      const me = await loginPassword.mutateAsync({
        email: email.trim(),
        password,
      });
      navigate(me.status === "Pending" ? "/cho-duyet" : safeFrom, {
        replace: true,
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Đăng nhập không thành công");
    }
  }

  return (
    <div className="o-li flex flex-1 items-center justify-center p-4">
      <div className="w-full max-w-sm rounded-card border border-grid bg-white p-6 sm:p-8">
        <div className="mb-6 flex flex-col items-center gap-2 text-center">
          <img src="/favicon.svg" alt="" className="size-12" />
          <h1 className="text-2xl font-semibold tracking-tight">
            Đăng nhập Học Liệu
          </h1>
          <p className="text-sm text-muted">
            Dùng tài khoản Google, hoặc email + mật khẩu
          </p>
        </div>

        {isInAppBrowser() ? (
          <InAppGuide />
        ) : import.meta.env.VITE_GOOGLE_CLIENT_ID ? (
          <div className="flex flex-col gap-3">
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
        ) : (
          <p className="rounded-btn border border-warn/40 bg-warn/10 p-3 text-sm text-ink">
            Chưa cấu hình{" "}
            <code className="font-mono text-xs">VITE_GOOGLE_CLIENT_ID</code> cho
            frontend — không hiển thị được nút đăng nhập Google.
          </p>
        )}

        <div className="my-4 flex items-center gap-3" aria-hidden>
          <span className="h-px flex-1 bg-grid" />
          <span className="text-xs text-muted">hoặc</span>
          <span className="h-px flex-1 bg-grid" />
        </div>

        <form onSubmit={submitPassword} className="flex flex-col gap-3">
          <label className="block text-sm">
            <span className="mb-1 block text-left text-xs font-medium text-muted">
              Email
            </span>
            <input
              type="email"
              name="email"
              autoComplete="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="w-full rounded-btn border border-grid bg-white px-3 py-2 text-sm outline-none transition focus:border-violet focus:ring-2 focus:ring-violet/20"
            />
          </label>
          <label className="block text-sm">
            <span className="mb-1 block text-left text-xs font-medium text-muted">
              Mật khẩu
            </span>
            <input
              type="password"
              name="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="w-full rounded-btn border border-grid bg-white px-3 py-2 text-sm outline-none transition focus:border-violet focus:ring-2 focus:ring-violet/20"
            />
          </label>
          <Button type="submit" disabled={loginPassword.isPending}>
            {loginPassword.isPending && (
              <Loader2 className="size-4 animate-spin" aria-hidden />
            )}
            Đăng nhập
          </Button>
        </form>

        {import.meta.env.DEV && (
          <div className="mt-4 rounded-btn border border-dashed border-grid p-3">
            <p className="mb-2 text-xs font-medium text-muted">
              Dev — đăng nhập nhanh (không cần Google)
            </p>
            <div className="flex gap-2">
              <Button
                variant="secondary"
                size="sm"
                className="flex-1"
                disabled={login.isPending}
                onClick={() =>
                  onCredential(
                    "devfake:e2e-e2eadminhoclieudev:e2e-admin@hoclieu.dev:Admin E2E",
                  )
                }
              >
                Admin
              </Button>
              <Button
                variant="secondary"
                size="sm"
                className="flex-1"
                disabled={login.isPending}
                onClick={() =>
                  onCredential(
                    "devfake:e2e-e2eteacherhoclieudev:e2e-teacher@hoclieu.dev:GV E2E",
                  )
                }
              >
                Giáo viên
              </Button>
            </div>
          </div>
        )}

        {error && (
          <p
            role="alert"
            className="mt-4 rounded-btn border border-wrong/40 bg-wrong/10 p-3 text-sm text-wrong"
          >
            {error}
          </p>
        )}

        <p className="mt-6 text-center text-xs text-muted">
          Học sinh và phụ huynh xem tài liệu, làm bài tập{" "}
          <Link to="/" className="text-violet underline">
            không cần tài khoản
          </Link>
          .
        </p>
      </div>
    </div>
  );
}

function RedirectHint({ dest }: { dest: string }) {
  const navigate = useNavigate();
  return (
    <div className="o-li flex flex-1 items-center justify-center p-4">
      <div className="w-full max-w-sm rounded-card border border-grid bg-white p-8 text-center">
        <Check className="mx-auto mb-3 size-8 text-correct" aria-hidden />
        <p className="text-sm text-ink">Bạn đã đăng nhập.</p>
        <Button
          className="mt-4"
          onClick={() => navigate(dest, { replace: true })}
        >
          Tiếp tục
        </Button>
      </div>
    </div>
  );
}

/** §3.1: hướng dẫn khi mở trong webview Zalo/Facebook (Google chặn đăng nhập). */
function InAppGuide() {
  const [copied, setCopied] = useState(false);

  function copyLink() {
    navigator.clipboard
      ?.writeText(window.location.origin + "/dang-nhap")
      .then(() => {
        setCopied(true);
        setTimeout(() => setCopied(false), 2000);
      })
      .catch(() => undefined);
  }

  return (
    <div className={cn("rounded-btn border border-violet/30 bg-violet/5 p-4")}>
      <p className="text-sm font-medium text-ink">
        Đang mở trong ứng dụng (Zalo/Facebook)?
      </p>
      <p className="mt-1 text-sm text-muted">
        Google không cho đăng nhập trong ứng dụng. Hãy mở trang đăng nhập bằng
        trình duyệt Chrome hoặc Safari, hoặc sao chép liên kết bên dưới:
      </p>
      <div className="mt-3 flex items-center gap-2">
        <code className="min-w-0 flex-1 truncate rounded-btn bg-white px-2 py-1 text-xs text-ink">
          {window.location.origin}/dang-nhap
        </code>
        <Button variant="secondary" size="sm" onClick={copyLink}>
          {copied ? (
            <Check className="size-3.5" aria-hidden />
          ) : (
            <Copy className="size-3.5" aria-hidden />
          )}
          {copied ? "Đã chép" : "Sao chép"}
        </Button>
      </div>
    </div>
  );
}
