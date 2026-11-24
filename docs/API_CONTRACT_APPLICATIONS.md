# 业务 API：社会救助申请（P1）

> 认证头：`Authorization: Bearer <sessionId>` + `X-NC-Device`。  
> Base：`https://cosmos-static.sua17.cn:20000`。  
> 所有金额字段为 decimal；日期为 ISO 8601。

## GET /api/applications

查询参数：

| 参数 | 默认 | 说明 |
|---|---|---|
| `page` | 1 | 从 1 开始 |
| `pageSize` | 20 | 1–100 |
| `keyword` | - | 姓名/身份证/家庭成员姓名/身份证 |
| `status` | - | `Draft` / `Submitted` / `Approved` / `Completed` / `Stopped` 等 |

响应：

```json
{
  "items": [
    {
      "id": 123,
      "applicationNo": "SA202611240001",
      "applicantName": "张三",
      "applicantIdCardMasked": "110***********1234",
      "applicantPhone": "138****0000",
      "status": "Draft",
      "statusText": "草稿",
      "currentStep": 2,
      "familySize": 3,
      "classificationResult": "低保",
      "totalAnnualIncome": 12000.00,
      "perCapitaAnnualIncome": 4000.00,
      "town": "柳树镇",
      "village": "测试村",
      "address": "柳树镇测试村1号",
      "createdAt": "2026-11-24T02:00:00Z",
      "updatedAt": "2026-11-24T03:00:00Z"
    }
  ],
  "pageIndex": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

## GET /api/applications/{id}

返回申请详情（用于详情页/表单回填）：

```json
{
  "id": 123,
  "applicationNo": "SA202611240001",
  "status": "Draft",
  "statusText": "草稿",
  "currentStep": 2,
  "applicantName": "张三",
  "applicantIdCard": "110101199001011234",
  "applicantPhone": "13800000000",
  "hukouType": "农业",
  "gender": "男",
  "ethnicity": "汉族",
  "hukouAddress": "柳树镇测试村1号",
  "townId": 10,
  "villageId": 100,
  "town": "柳树镇",
  "village": "测试村",
  "address": "柳树镇测试村1号",
  "familySize": 3,
  "classificationResult": "低保",
  "totalAnnualIncome": 12000.00,
  "perCapitaAnnualIncome": 4000.00,
  "totalFamilyIncome": 1000.00,
  "perCapitaIncome": 333.33,
  "householdMonthlyGuaranteeAmount": 900.00,
  "applicationReason": "因病",
  "applicationReasonDetail": "长期治疗",
  "submitAt": null,
  "createdAt": "2026-11-24T02:00:00Z",
  "updatedAt": "2026-11-24T03:00:00Z",
  "members": [
    {
      "id": 1,
      "name": "张三",
      "idCard": "110101199001011234",
      "relation": "户主",
      "gender": "男",
      "birthDate": "1990-01-01",
      "age": 36,
      "healthStatus": "健康",
      "employmentStatus": "务工",
      "monthlyIncome": 1000.00
    }
  ]
}
```

## 错误

沿用 P0：

```json
{ "code": "AUTH_REQUIRED", "message": "请先登录", "traceId": "..." }
```

- 401：未登录/会话失效；
- 403：无权限（P1 暂按登录用户放行，后续补权限码）；
- 404：申请不存在；
- 500：数据库/服务端错误。
