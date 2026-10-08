import { useEffect, useState } from "react";
import { useAdminSaveSettings, useAdminSettings } from "@/features/admin/api";
import type { SettingsMap } from "@/features/admin/types";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";

/** /admin/cai-dat — 9 key cài đặt hệ thống (spec §5.4). */
export function AdminSettingsPage() {
  const { data, isPending } = useAdminSettings();
  const save = useAdminSaveSettings();
  const [form, setForm] = useState<FormState | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState<string | null>(null);

  useEffect(() => {
    if (data?.settings && !form) setForm(fromSettings(data.settings));
  }, [data, form]);

  if (isPending || !form) {
    return (
      <div className="space-y-3">
        {Array.from({ length: 5 }, (_, i) => (
          <Skeleton key={i} className="h-16" />
        ))}
      </div>
    );
  }

  const submit = async () => {
    setError(null);
    setSaved(null);
    let contact: unknown = {};
    try {
      contact = JSON.parse(form.siteContact || "{}");
    } catch {
      setError("Liên hệ (site.contact) phải là JSON hợp lệ.");
      return;
    }
    const maxMb = Number(form.maxMb);
    if (!Number.isFinite(maxMb) || maxMb < 1 || maxMb > 500) {
      setError("Dung lượng tối đa phải là số từ 1 đến 500 MB.");
      return;
    }
    const settings: SettingsMap = {
      "site.name": form.siteName,
      "site.logo_file_id": form.siteLogoFileId || null,
      "site.contact": contact,
    };
    settings["auth.auto_approve_domains"] = form.autoApproveDomains
      .split("\n")
      .map((x) => x.trim())
      .filter(Boolean);
    settings["content.require_review"] = form.requireReview;
    settings["content.show_author_public"] = form.showAuthorPublic;
    settings["download.guest_default"] = form.guestDefault;
    settings["upload.max_mb"] = maxMb;
    settings["upload.allowed_ext"] = form.allowedExt
      .split(",")
      .map((x) => x.trim())
      .filter(Boolean);
    try {
      const res = await save.mutateAsync(settings);
      setSaved(`Đã lưu: ${res.updated.join(", ")}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không lưu được cài đặt");
    }
  };

  const field =
    "h-10 w-full rounded-btn border border-grid bg-white px-3 text-sm outline-none focus:border-violet";

  return (
    <div className="max-w-2xl space-y-5">
      <h1 className="text-xl font-semibold">Cài đặt</h1>

      <section className="rounded-card border border-grid bg-white p-4">
        <h2 className="mb-3 text-sm font-semibold">Khối trang</h2>
        <div className="grid gap-3 sm:grid-cols-2">
          <label className="block">
            <span className="mb-1 block text-sm font-medium">Tên site</span>
            <input
              type="text"
              value={form.siteName}
              onChange={(e) =>
                setForm((f) => f && { ...f, siteName: e.target.value })
              }
              className={field}
            />
          </label>
          <label className="block">
            <span className="mb-1 block text-sm font-medium">
              Logo (public_id Cloudinary)
            </span>
            <input
              type="text"
              value={form.siteLogoFileId}
              onChange={(e) =>
                setForm((f) => f && { ...f, siteLogoFileId: e.target.value })
              }
              placeholder="trống = không có logo"
              className={field}
            />
          </label>
          <label className="block sm:col-span-2">
            <span className="mb-1 block text-sm font-medium">
              Liên hệ (JSON, VD:{" "}
              <code>{'{"email": "contact@truong.edu.vn"}'}</code>)
            </span>
            <textarea
              rows={2}
              value={form.siteContact}
              onChange={(e) =>
                setForm((f) => f && { ...f, siteContact: e.target.value })
              }
              className="w-full rounded-btn border border-grid p-2 font-mono text-sm outline-none focus:border-violet"
            />
          </label>
        </div>
      </section>

      <section className="rounded-card border border-grid bg-white p-4">
        <h2 className="mb-3 text-sm font-semibold">Đăng nhập & nội dung</h2>
        <label className="block">
          <span className="mb-1 block text-sm font-medium">
            Domain Google tự duyệt (mỗi dòng 1 domain, trống = không tự duyệt)
          </span>
          <textarea
            rows={2}
            value={form.autoApproveDomains}
            onChange={(e) =>
              setForm((f) => f && { ...f, autoApproveDomains: e.target.value })
            }
            placeholder="truong.edu.vn"
            className="mb-3 w-full rounded-btn border border-grid p-2 text-sm outline-none focus:border-violet"
          />
        </label>
        <div className="space-y-2">
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={form.requireReview}
              onChange={(e) =>
                setForm((f) => f && { ...f, requireReview: e.target.checked })
              }
              className="size-4 accent-violet"
            />
            Bật kiểm duyệt nội dung công khai (Tổ trưởng/Admin duyệt trước khi
            hiện)
          </label>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={form.showAuthorPublic}
              onChange={(e) =>
                setForm(
                  (f) => f && { ...f, showAuthorPublic: e.target.checked },
                )
              }
              className="size-4 accent-violet"
            />
            Hiện tên tác giả ở khu công khai
          </label>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={form.guestDefault}
              onChange={(e) =>
                setForm((f) => f && { ...f, guestDefault: e.target.checked })
              }
              className="size-4 accent-violet"
            />
            Mặc định cho phép khách tải file (tạo tài liệu mới)
          </label>
        </div>
      </section>

      <section className="rounded-card border border-grid bg-white p-4">
        <h2 className="mb-3 text-sm font-semibold">Tải file lên</h2>
        <div className="grid gap-3 sm:grid-cols-2">
          <label className="block">
            <span className="mb-1 block text-sm font-medium">
              Dung lượng tối đa (MB, 1–500)
            </span>
            <input
              type="number"
              min={1}
              max={500}
              value={form.maxMb}
              onChange={(e) =>
                setForm((f) => f && { ...f, maxMb: e.target.value })
              }
              className={field}
            />
          </label>
          <label className="block">
            <span className="mb-1 block text-sm font-medium">
              Định dạng cho phép (phân tách dấu phẩy)
            </span>
            <input
              type="text"
              value={form.allowedExt}
              onChange={(e) =>
                setForm((f) => f && { ...f, allowedExt: e.target.value })
              }
              className={field}
            />
          </label>
        </div>
      </section>

      {error ? (
        <p className="text-sm text-redpen" role="alert">
          {error}
        </p>
      ) : null}
      {saved ? (
        <p className="text-sm text-correct" role="status">
          {saved}
        </p>
      ) : null}
      <div className="flex justify-end">
        <Button onClick={() => void submit()} disabled={save.isPending}>
          {save.isPending ? "Đang lưu…" : "Lưu cài đặt"}
        </Button>
      </div>
    </div>
  );
}

interface FormState {
  siteName: string;
  siteLogoFileId: string;
  siteContact: string;
  autoApproveDomains: string;
  requireReview: boolean;
  showAuthorPublic: boolean;
  guestDefault: boolean;
  maxMb: string;
  allowedExt: string;
}

function fromSettings(s: SettingsMap): FormState {
  const str = (k: string) => (typeof s[k] === "string" ? (s[k] as string) : "");
  return {
    siteName: str("site.name"),
    siteLogoFileId: str("site.logo_file_id"),
    siteContact:
      s["site.contact"] != null && typeof s["site.contact"] === "object"
        ? JSON.stringify(s["site.contact"], null, 2)
        : "{}",
    autoApproveDomains: Array.isArray(s["auth.auto_approve_domains"])
      ? (s["auth.auto_approve_domains"] as string[]).join("\n")
      : "",
    requireReview: s["content.require_review"] === true,
    showAuthorPublic: s["content.show_author_public"] !== false,
    guestDefault: s["download.guest_default"] === true,
    maxMb:
      typeof s["upload.max_mb"] === "number"
        ? String(s["upload.max_mb"])
        : "50",
    allowedExt: Array.isArray(s["upload.allowed_ext"])
      ? (s["upload.allowed_ext"] as string[]).join(", ")
      : "pdf, docx, pptx, xlsx, jpg, png",
  };
}
