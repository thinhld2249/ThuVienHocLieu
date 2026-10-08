import { useState, type FormEvent } from "react";
import { Check, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  useLogoutAll,
  useMe,
  useSetPassword,
  useUpdateMe,
} from "@/features/auth/api";
import { teamRoleLabel } from "@/features/teams/labels";

const inputCls =
  "w-full rounded-btn border border-grid bg-white px-3 py-2 text-sm outline-none transition focus:border-violet focus:ring-2 focus:ring-violet/20";

/**
 * §5.2: Hồ sơ — họ tên, SĐT, tổ, đặt mật khẩu đăng nhập,
 * "Đăng xuất mọi thiết bị".
 */
export function GvHoSoPage() {
  const { data: me } = useMe();
  const updateMe = useUpdateMe();
  const setPassword = useSetPassword();
  const logoutAll = useLogoutAll();

  const [fullName, setFullName] = useState(me?.fullName ?? "");
  const [phone, setPhone] = useState(me?.phone ?? "");
  const [profileError, setProfileError] = useState<string | null>(null);
  const [profileSaved, setProfileSaved] = useState(false);

  const [newPassword, setNewPassword] = useState("");
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [passwordSaved, setPasswordSaved] = useState(false);

  if (!me) return null; // RequireActive đã chặn khi chưa đăng nhập

  async function saveProfile(e: FormEvent) {
    e.preventDefault();
    setProfileError(null);
    setProfileSaved(false);
    try {
      await updateMe.mutateAsync({ fullName, phone });
      setProfileSaved(true);
    } catch (err) {
      setProfileError(
        err instanceof Error ? err.message : "Không lưu được thông tin",
      );
    }
  }

  async function savePassword(e: FormEvent) {
    e.preventDefault();
    setPasswordError(null);
    setPasswordSaved(false);
    try {
      await setPassword.mutateAsync(newPassword);
      setNewPassword("");
      setPasswordSaved(true);
    } catch (err) {
      setPasswordError(
        err instanceof Error ? err.message : "Không đặt được mật khẩu",
      );
    }
  }

  return (
    <div className="mx-auto max-w-2xl">
      <h1 className="mb-6 text-2xl font-semibold tracking-tight">Hồ sơ</h1>

      <section className="rounded-card border border-grid bg-white p-5">
        <div className="mb-4 flex items-center gap-3">
          {me.avatarUrl ? (
            <img
              src={me.avatarUrl}
              alt=""
              className="size-14 rounded-full"
              referrerPolicy="no-referrer"
            />
          ) : (
            <span className="flex size-14 items-center justify-center rounded-full bg-violet/10 text-xl font-semibold text-violet">
              {me.fullName.charAt(0).toUpperCase()}
            </span>
          )}
          <div>
            <p className="font-medium">{me.fullName}</p>
            <p className="text-sm text-muted">{me.email}</p>
          </div>
        </div>

        <form onSubmit={saveProfile} className="flex flex-col gap-3">
          <label className="block text-sm">
            <span className="mb-1 block text-xs font-medium text-muted">
              Họ và tên
            </span>
            <input
              value={fullName}
              onChange={(e) => setFullName(e.target.value)}
              required
              minLength={2}
              maxLength={200}
              className={inputCls}
            />
          </label>
          <label className="block text-sm">
            <span className="mb-1 block text-xs font-medium text-muted">
              Số điện thoại
            </span>
            <input
              type="tel"
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              className={inputCls}
            />
          </label>
          <div className="flex items-center gap-3">
            <Button type="submit" disabled={updateMe.isPending}>
              {updateMe.isPending && (
                <Loader2 className="size-4 animate-spin" aria-hidden />
              )}
              Lưu thông tin
            </Button>
            {profileSaved && (
              <span className="inline-flex items-center gap-1 text-sm text-correct">
                <Check className="size-4" aria-hidden /> Đã lưu
              </span>
            )}
          </div>
          {profileError && (
            <p role="alert" className="text-sm text-wrong">
              {profileError}
            </p>
          )}
        </form>
      </section>

      <section className="mt-4 rounded-card border border-grid bg-white p-5">
        <h2 className="text-sm font-semibold">Tổ chuyên môn</h2>
        {me.teams.length === 0 ? (
          <p className="mt-2 text-sm text-muted">Chưa tham gia tổ nào.</p>
        ) : (
          <ul className="mt-2 space-y-1">
            {me.teams.map((t) => (
              <li key={t.id} className="text-sm">
                <span className="font-medium">{t.name}</span>
                <span className="text-muted"> — {teamRoleLabel(t.role)}</span>
              </li>
            ))}
          </ul>
        )}
      </section>

      {me.systemRole === "Admin" && (
        <section className="mt-4 rounded-card border border-grid bg-white p-5">
          <h2 className="text-sm font-semibold">Mật khẩu đăng nhập</h2>
          <p className="mt-1 text-sm text-muted">
            Đặt mật khẩu để đăng nhập bằng email + mật khẩu, ngoài nút Google
            (chỉ dành cho tài khoản Admin).
          </p>
          <form onSubmit={savePassword} className="mt-3 flex flex-col gap-3">
            <label className="block text-sm">
              <span className="mb-1 block text-xs font-medium text-muted">
                Mật khẩu mới (từ 8 ký tự)
              </span>
              <input
                type="password"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                required
                minLength={8}
                maxLength={128}
                autoComplete="new-password"
                className={inputCls}
              />
            </label>
            <div className="flex flex-wrap items-center gap-3">
              <Button
                type="submit"
                variant="secondary"
                disabled={setPassword.isPending}
              >
                {setPassword.isPending && (
                  <Loader2 className="size-4 animate-spin" aria-hidden />
                )}
                Đặt mật khẩu
              </Button>
              {passwordSaved && (
                <span className="inline-flex items-center gap-1 text-sm text-correct">
                  <Check className="size-4" aria-hidden /> Đã đặt — có thể đăng
                  nhập bằng email + mật khẩu
                </span>
              )}
            </div>
            {passwordError && (
              <p role="alert" className="text-sm text-wrong">
                {passwordError}
              </p>
            )}
          </form>
        </section>
      )}

      <section className="mt-4 rounded-card border border-wrong/30 bg-white p-5">
        <h2 className="text-sm font-semibold text-wrong">Phiên đăng nhập</h2>
        <p className="mt-1 text-sm text-muted">
          Đẩy mình ra khỏi mọi thiết bị — mọi phiên cũ hết hiệu lực, phải đăng
          nhập lại.
        </p>
        <Button
          variant="outline"
          className="mt-3"
          disabled={logoutAll.isPending}
          onClick={() => {
            if (
              window.confirm(
                "Đăng xuất trên mọi thiết bị? Bạn phải đăng nhập lại.",
              )
            )
              logoutAll.mutate();
          }}
        >
          {logoutAll.isPending && (
            <Loader2 className="size-4 animate-spin" aria-hidden />
          )}
          Đăng xuất mọi thiết bị
        </Button>
      </section>
    </div>
  );
}
