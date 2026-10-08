import { useState } from "react";
import { Link } from "react-router";
import { Plus, Tags, X } from "lucide-react";
import {
  useAdminCreateSchoolYear,
  useAdminDeactivateSubject,
  useAdminDeleteSection,
  useAdminDeleteTag,
  useAdminGrades,
  useAdminSaveGrade,
  useAdminSaveSection,
  useAdminSaveSubject,
  useAdminSaveTag,
  useAdminSchoolYears,
  useAdminSections,
  useAdminSubjects,
  useAdminTags,
  useAdminUpdateGrade,
} from "@/features/admin/api";
import type {
  GradeAdmin,
  SectionAdmin,
  SubjectAdmin,
  TagDto,
} from "@/features/admin/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";

/** /admin/danh-muc — chuyên mục, khối, môn học, năm học, tags (spec §5.4). */
export function AdminTaxonomyPage() {
  const { data: sections, isPending } = useAdminSections();
  const { data: grades } = useAdminGrades();
  const { data: subjects } = useAdminSubjects();
  const { data: years } = useAdminSchoolYears();
  const { data: tags } = useAdminTags();
  const createYear = useAdminCreateSchoolYear();
  const [yearName, setYearName] = useState("");
  const [yearStart, setYearStart] = useState("2026-09-05");
  const [yearEnd, setYearEnd] = useState("2027-05-31");

  if (isPending) {
    return (
      <div className="space-y-3">
        {Array.from({ length: 5 }, (_, i) => (
          <Skeleton key={i} className="h-24" />
        ))}
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <h1 className="text-xl font-semibold">Danh mục</h1>

      <section>
        <h2 className="mb-2 text-base font-semibold">Chuyên mục</h2>
        <div className="space-y-2">
          {(sections ?? []).map((s) => (
            <SectionEditor key={s.id} section={s} />
          ))}
        </div>
        <NewSectionForm />
      </section>

      <section>
        <h2 className="mb-2 text-base font-semibold">Khối</h2>
        <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
          {(grades ?? []).map((g) => (
            <GradeEditor key={g.id} grade={g} />
          ))}
        </div>
        <NewGradeForm />
      </section>

      <section>
        <h2 className="mb-2 text-base font-semibold">Môn học</h2>
        <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
          {(subjects ?? []).map((s) => (
            <SubjectRow key={s.id} subject={s} />
          ))}
        </div>
        <NewSubjectForm />
      </section>

      <section>
        <h2 className="mb-2 text-base font-semibold">Năm học</h2>
        <div className="overflow-x-auto rounded-card border border-grid bg-white">
          <table className="w-full min-w-[520px] text-sm">
            <thead className="border-b border-grid bg-paper text-left text-xs text-muted">
              <tr>
                <th className="p-2">Năm học</th>
                <th className="p-2">Bắt đầu</th>
                <th className="p-2">Kết thúc</th>
                <th className="p-2">Trạng thái</th>
              </tr>
            </thead>
            <tbody>
              {(years ?? []).map((y) => (
                <tr
                  key={y.id}
                  className="border-b border-grid/60 last:border-0"
                >
                  <td className="p-2 font-medium">{y.name}</td>
                  <td className="p-2 text-muted">{y.startDate}</td>
                  <td className="p-2 text-muted">{y.endDate}</td>
                  <td className="p-2">
                    {y.isCurrent ? (
                      <Badge className="bg-correct/10 text-correct">
                        Năm hiện tại
                      </Badge>
                    ) : (
                      <span className="text-muted">Lưu trữ</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p className="mt-1.5 text-xs text-muted">
          Kết chuyển năm học (lưu trữ lớp cũ, nhân bản lên khối +1) tại{" "}
          <Link to="/admin/nam-hoc" className="text-violet hover:underline">
            Năm học
          </Link>
          .
        </p>
        <form
          className="mt-3 flex flex-wrap items-end gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            void createYear
              .mutateAsync({
                name: yearName.trim(),
                startDate: yearStart,
                endDate: yearEnd,
              })
              .then(() => {
                setYearName("");
              });
          }}
        >
          <label className="block">
            <span className="mb-1 block text-xs font-medium text-muted">
              Tên năm học
            </span>
            <input
              type="text"
              required
              value={yearName}
              onChange={(e) => setYearName(e.target.value)}
              placeholder="VD: 2027-2028"
              className="h-10 w-40 rounded-btn border border-grid bg-white px-3 text-sm outline-none focus:border-violet"
            />
          </label>
          <label className="block">
            <span className="mb-1 block text-xs font-medium text-muted">
              Bắt đầu
            </span>
            <input
              type="date"
              required
              value={yearStart}
              onChange={(e) => setYearStart(e.target.value)}
              className="h-10 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
            />
          </label>
          <label className="block">
            <span className="mb-1 block text-xs font-medium text-muted">
              Kết thúc
            </span>
            <input
              type="date"
              required
              value={yearEnd}
              onChange={(e) => setYearEnd(e.target.value)}
              className="h-10 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
            />
          </label>
          <Button type="submit" disabled={createYear.isPending}>
            <Plus className="size-4" aria-hidden /> Tạo năm học
          </Button>
        </form>
      </section>

      <section>
        <h2 className="mb-2 text-base font-semibold">Tags</h2>
        <TagList tags={tags ?? []} />
      </section>
    </div>
  );
}

// ===== Chuyên mục =====

function SectionEditor({ section }: { section: SectionAdmin }) {
  const save = useAdminSaveSection();
  const remove = useAdminDeleteSection();
  const [form, setForm] = useState({
    name: section.name,
    sort: String(section.sort),
    color: section.color ?? "",
    defaultPublishMode: section.defaultPublishMode,
    defaultScope: section.defaultScope,
    requireWeek: section.requireWeek,
    isInternal: section.isInternal,
  });
  const dirty =
    form.name !== section.name ||
    form.sort !== String(section.sort) ||
    form.color !== (section.color ?? "") ||
    form.defaultPublishMode !== section.defaultPublishMode ||
    form.defaultScope !== section.defaultScope ||
    form.requireWeek !== section.requireWeek ||
    form.isInternal !== section.isInternal;

  const field =
    "h-9 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet";

  return (
    <div className="flex flex-wrap items-center gap-2 rounded-card border border-grid bg-white p-2">
      <input
        aria-label="Tên chuyên mục"
        value={form.name}
        onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
        className={`${field} w-48`}
      />
      <input
        aria-label="Thứ tự"
        type="number"
        value={form.sort}
        onChange={(e) => setForm((f) => ({ ...f, sort: e.target.value }))}
        className={`${field} w-16`}
      />
      <input
        aria-label="Màu (hex)"
        value={form.color}
        onChange={(e) => setForm((f) => ({ ...f, color: e.target.value }))}
        placeholder="#2f6fdb"
        className={`${field} w-24`}
      />
      <select
        aria-label="Mặc định hiển thị"
        value={form.defaultPublishMode}
        onChange={(e) =>
          setForm((f) => ({ ...f, defaultPublishMode: e.target.value }))
        }
        className={field}
      >
        <option value="Visible">Hiện</option>
        <option value="Hidden">Ẩn</option>
      </select>
      <select
        aria-label="Mặc định phạm vi"
        value={form.defaultScope}
        onChange={(e) =>
          setForm((f) => ({ ...f, defaultScope: e.target.value }))
        }
        className={field}
      >
        <option value="Public">Công khai</option>
        <option value="Teachers">Giáo viên</option>
        <option value="Team">Tổ</option>
        <option value="Private">Riêng tư</option>
      </select>
      <label className="flex items-center gap-1 text-xs text-muted">
        <input
          type="checkbox"
          checked={form.requireWeek}
          onChange={(e) =>
            setForm((f) => ({ ...f, requireWeek: e.target.checked }))
          }
          className="size-3.5 accent-violet"
        />
        Bắt buộc tuần
      </label>
      <label className="flex items-center gap-1 text-xs text-muted">
        <input
          type="checkbox"
          checked={form.isInternal}
          onChange={(e) =>
            setForm((f) => ({ ...f, isInternal: e.target.checked }))
          }
          className="size-3.5 accent-violet"
        />
        Nội bộ tổ
      </label>
      <div className="ml-auto flex gap-1">
        <Button
          size="sm"
          disabled={!dirty || save.isPending}
          onClick={() =>
            void save
              .mutateAsync({
                id: section.id,
                name: form.name.trim(),
                sort: Number(form.sort) || section.sort,
                color: form.color || null,
                defaultPublishMode: form.defaultPublishMode,
                defaultScope: form.defaultScope,
                requireWeek: form.requireWeek,
                isInternal: form.isInternal,
                isActive: section.isActive,
              } as Partial<SectionAdmin> & { name: string })
              .then(() => setForm((f) => ({ ...f, name: f.name })))
          }
        >
          Lưu
        </Button>
        {section.isActive ? (
          <Button
            size="sm"
            variant="outline"
            disabled={remove.isPending}
            onClick={() => {
              if (window.confirm(`Tắt chuyên mục "${section.name}"?`))
                void remove.mutateAsync(section.id);
            }}
          >
            Tắt
          </Button>
        ) : (
          <Button
            size="sm"
            variant="outline"
            disabled={save.isPending}
            onClick={() =>
              void save.mutateAsync({
                id: section.id,
                name: section.name,
                isActive: true,
              } as Partial<SectionAdmin> & { name: string })
            }
          >
            Bật lại
          </Button>
        )}
      </div>
    </div>
  );
}

function NewSectionForm() {
  const save = useAdminSaveSection();
  const [name, setName] = useState("");
  const [color, setColor] = useState("");
  return (
    <form
      className="mt-2 flex flex-wrap items-center gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        if (!name.trim()) return;
        void save
          .mutateAsync({ name: name.trim(), color: color || null })
          .then(() => {
            setName("");
            setColor("");
          });
      }}
    >
      <input
        aria-label="Tên chuyên mục mới"
        value={name}
        onChange={(e) => setName(e.target.value)}
        placeholder="Tên chuyên mục mới"
        className="h-9 w-48 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
      />
      <input
        aria-label="Màu chuyên mục mới"
        value={color}
        onChange={(e) => setColor(e.target.value)}
        placeholder="#2f6fdb"
        className="h-9 w-24 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
      />
      <Button
        type="submit"
        variant="outline"
        size="sm"
        disabled={save.isPending}
      >
        <Plus className="size-3.5" aria-hidden /> Thêm
      </Button>
    </form>
  );
}

// ===== Khối =====

function GradeEditor({ grade }: { grade: GradeAdmin }) {
  const update = useAdminUpdateGrade();
  const [name, setName] = useState(grade.name);
  const [sort, setSort] = useState(String(grade.sort));
  const dirty = name !== grade.name || sort !== String(grade.sort);
  return (
    <div className="flex items-center gap-2 rounded-card border border-grid bg-white p-2">
      <input
        aria-label={`Tên khối ${grade.id}`}
        value={name}
        onChange={(e) => setName(e.target.value)}
        className="h-9 min-w-0 flex-1 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
      />
      <input
        aria-label="Thứ tự"
        type="number"
        value={sort}
        onChange={(e) => setSort(e.target.value)}
        className="h-9 w-16 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
      />
      <Button
        size="sm"
        disabled={!dirty || update.isPending}
        onClick={() =>
          void update.mutateAsync({
            id: grade.id,
            name: name.trim(),
            sort: Number(sort) || grade.sort,
          })
        }
      >
        Lưu
      </Button>
    </div>
  );
}

function NewGradeForm() {
  const save = useAdminSaveGrade();
  const [name, setName] = useState("");
  const [sort, setSort] = useState("");
  return (
    <form
      className="mt-2 flex flex-wrap items-center gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        if (!name.trim()) return;
        void save
          .mutateAsync({
            name: name.trim(),
            sort: sort ? Number(sort) : undefined,
          })
          .then(() => {
            setName("");
            setSort("");
          });
      }}
    >
      <input
        aria-label="Tên khối mới"
        value={name}
        onChange={(e) => setName(e.target.value)}
        placeholder="Tên khối (VD: 6)"
        className="h-9 w-36 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
      />
      <input
        aria-label="Thứ tự khối mới"
        type="number"
        value={sort}
        onChange={(e) => setSort(e.target.value)}
        placeholder="Thứ tự"
        className="h-9 w-20 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
      />
      <Button
        type="submit"
        variant="outline"
        size="sm"
        disabled={save.isPending}
      >
        <Plus className="size-3.5" aria-hidden /> Thêm
      </Button>
    </form>
  );
}

// ===== Môn học =====

function SubjectRow({ subject }: { subject: SubjectAdmin }) {
  const deactivate = useAdminDeactivateSubject();
  return (
    <div className="flex items-center justify-between gap-2 rounded-card border border-grid bg-white p-2">
      <div className="min-w-0">
        <div className="truncate text-sm font-medium">{subject.name}</div>
        {subject.isActive ? null : (
          <Badge className="bg-muted/10 text-muted">Đã tắt</Badge>
        )}
      </div>
      {subject.isActive ? (
        <Button
          size="sm"
          variant="outline"
          disabled={deactivate.isPending}
          onClick={() => {
            if (window.confirm(`Tắt môn "${subject.name}"?`))
              void deactivate.mutateAsync(subject.id);
          }}
        >
          Tắt
        </Button>
      ) : (
        <span className="text-xs text-muted">—</span>
      )}
    </div>
  );
}

function NewSubjectForm() {
  const save = useAdminSaveSubject();
  const [name, setName] = useState("");
  return (
    <form
      className="mt-2 flex flex-wrap items-center gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        if (!name.trim()) return;
        void save.mutateAsync({ name: name.trim() }).then(() => setName(""));
      }}
    >
      <input
        aria-label="Tên môn học mới"
        value={name}
        onChange={(e) => setName(e.target.value)}
        placeholder="Tên môn học mới"
        className="h-9 w-56 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
      />
      <Button
        type="submit"
        variant="outline"
        size="sm"
        disabled={save.isPending}
      >
        <Plus className="size-3.5" aria-hidden /> Thêm
      </Button>
    </form>
  );
}

// ===== Tags =====

function TagList({ tags }: { tags: TagDto[] }) {
  const add = useAdminSaveTag();
  const remove = useAdminDeleteTag();
  const [name, setName] = useState("");
  return (
    <div>
      <div className="flex flex-wrap gap-1.5">
        {tags.length === 0 ? (
          <p className="text-sm text-muted">Chưa có tag nào.</p>
        ) : (
          tags.map((t) => (
            <span
              key={t.id}
              className="flex items-center gap-1 rounded-btn border border-grid bg-white px-2 py-1 text-sm"
            >
              {t.name}
              <button
                type="button"
                aria-label={`Xóa tag ${t.name}`}
                disabled={remove.isPending}
                onClick={() => void remove.mutateAsync(t.id)}
                className="text-muted hover:text-wrong"
              >
                <X className="size-3.5" aria-hidden />
              </button>
            </span>
          ))
        )}
      </div>
      <form
        className="mt-2 flex items-center gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          if (!name.trim()) return;
          void add.mutateAsync(name.trim()).then(() => setName(""));
        }}
      >
        <span className="text-muted">
          <Tags className="size-4" aria-hidden />
        </span>
        <input
          aria-label="Tag mới"
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="Tag mới"
          className="h-9 w-40 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
        />
        <Button
          type="submit"
          variant="outline"
          size="sm"
          disabled={add.isPending}
        >
          Thêm
        </Button>
      </form>
    </div>
  );
}
