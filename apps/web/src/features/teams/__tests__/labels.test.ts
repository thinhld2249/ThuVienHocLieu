import { describe, expect, it } from "vitest";
import {
  invitationResultInfo,
  invitationStatusVariant,
  parseEmails,
  teamRoleLabel,
  teamRoleVariant,
} from "../labels";

describe("teamRoleLabel / teamRoleVariant", () => {
  it("đặt nhãn tiếng Việt đúng vai trò", () => {
    expect(teamRoleLabel("Lead")).toBe("Tổ trưởng");
    expect(teamRoleLabel("Deputy")).toBe("Tổ phó");
    expect(teamRoleLabel("Member")).toBe("Thành viên");
    expect(teamRoleLabel(null)).toBe("Khách");
  });

  it("Lead = mặc định tím, Deputy = xanh, Member = viền", () => {
    expect(teamRoleVariant("Lead")).toBe("default");
    expect(teamRoleVariant("Deputy")).toBe("success");
    expect(teamRoleVariant("Member")).toBe("outline");
  });
});

describe("invitationStatusVariant", () => {
  it("gán màu theo 4 trạng thái tiếng Việt", () => {
    expect(invitationStatusVariant("Chờ")).toBe("default");
    expect(invitationStatusVariant("Đã nhận")).toBe("success");
    expect(invitationStatusVariant("Hết hạn")).toBe("warning");
    expect(invitationStatusVariant("Đã thu hồi")).toBe("destructive");
    expect(invitationStatusVariant("??")).toBe("outline");
  });
});

describe("invitationResultInfo", () => {
  it("nhãn + tone theo từng action", () => {
    expect(invitationResultInfo({ email: "a@b.c", action: "added", reason: null, invitationId: null, link: null }).label).toBe(
      "Đã thêm vào tổ",
    );
    expect(invitationResultInfo({ email: "a@b.c", action: "invited", reason: null, invitationId: null, link: null }).tone).toBe(
      "ok",
    );
    expect(invitationResultInfo({ email: "a@b.c", action: "existing", reason: "Đã là thành viên của tổ.", invitationId: null, link: null })).toEqual(
      { label: "Đã là thành viên của tổ.", tone: "info" },
    );
    expect(invitationResultInfo({ email: "a@b.c", action: "skipped", reason: "Tài khoản đã bị khóa.", invitationId: null, link: null })).toEqual(
      { label: "Tài khoản đã bị khóa.", tone: "warn" },
    );
  });
});

describe("parseEmails", () => {
  it("tách theo xuống dòng, phẩy, chấm phẩy — bỏ ô rác, bỏ trùng", () => {
    expect(
      parseEmails("a@truong.edu.vn, b@gmail.com;\nA@truong.edu.vn\nc@yahoo.com\nrác rưởi"),
    ).toEqual(["a@truong.edu.vn", "b@gmail.com", "c@yahoo.com"]);
  });

  it("bỏ khoảng trắng thừa", () => {
    expect(parseEmails("  x@y.z  \n  x@y.z  ")).toEqual(["x@y.z"]);
  });

  it("trống → mảng rỗng", () => {
    expect(parseEmails("")).toEqual([]);
  });
});
