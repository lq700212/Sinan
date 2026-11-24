import 'package:flutter_test/flutter_test.dart';
import 'package:sinan_mobile/models/issue.dart';
import 'package:sinan_mobile/models/rules.dart';

/// 纯规则回归：这些口径必须与桌面端 `Sinan.Core/Utils` 完全一致，
/// 否则双端统计会对不上。改规则先改这里。
void main() {
  group('parseRawDate', () {
    test('两位月日按基准年补全', () {
      expect(Rules.parseRawDate('11.5', baseYear: 2026), DateTime(2026, 11, 5));
      expect(Rules.parseRawDate('11-25', baseYear: 2026), DateTime(2026, 11, 25));
      expect(Rules.parseRawDate('11/5', baseYear: 2026), DateTime(2026, 11, 5));
    });

    test('完整年月日', () {
      expect(Rules.parseRawDate('2026.11.5'), DateTime(2026, 11, 5));
      expect(Rules.parseRawDate('2026年11月5日'), DateTime(2026, 11, 5));
    });

    test('/ 与已下图不是日期', () {
      expect(Rules.parseRawDate('/'), isNull);
      expect(Rules.parseRawDate('已下图'), isNull);
      expect(Rules.parseRawDate(''), isNull);
    });

    test('非法月日不解析', () {
      expect(Rules.parseRawDate('13.5', baseYear: 2026), isNull);
      expect(Rules.parseRawDate('11.40', baseYear: 2026), isNull);
    });
  });

  group('deriveStatus', () {
    List<IssueStage> stages(Map<String, StageState> states) => [
          for (final code in StageCodes.all)
            IssueStage(
              code: code,
              ownerText: '',
              dueRaw: '',
              state: states[code] ?? StageState.notStarted,
            ),
        ];

    test('全空是待分派', () {
      expect(Rules.deriveStatus(stages({})), IssueStatuses.unassigned);
    });

    test('方案完成后进入设计中', () {
      expect(
        Rules.deriveStatus(stages({StageCodes.plan: StageState.done})),
        IssueStatuses.designing,
      );
    });

    test('前序已处理时不停留在待分派', () {
      expect(
        Rules.deriveStatus(stages({
          StageCodes.plan: StageState.done,
          StageCodes.design: StageState.done,
        })),
        IssueStatuses.material,
      );
    });

    test('全部完成进入待验证', () {
      expect(
        Rules.deriveStatus(stages({
          for (final code in StageCodes.all) code: StageState.done,
        })),
        IssueStatuses.verifying,
      );
    });

    test('有完成说明进入待验证', () {
      expect(
        Rules.deriveStatus(stages({}), completionText: '现场已确认'),
        IssueStatuses.verifying,
      );
    });
  });

  group('resolveSubproject', () {
    test('机台号段归组', () {
      expect(Rules.resolveSubproject('P7391101'), 'P7391-01');
      expect(Rules.resolveSubproject('P7391205'), 'P7391-02');
      expect(Rules.resolveSubproject('P7391309'), 'P7391-03');
    });

    test('位数异常留待确认', () {
      expect(Rules.resolveSubproject('P73913118'), isNull);
    });
  });

  group('风险口径', () {
    Issue issueWith({
      required String status,
      required DateTime designDue,
      required DateTime assemblyDue,
      StageState designState = StageState.inProgress,
    }) {
      return Issue(
        id: 'X1',
        seqNo: '1',
        machineCode: 'P7391101',
        subprojectCode: 'P7391-01',
        title: '测试问题',
        solution: '',
        status: status,
        stages: [
          IssueStage(
            code: StageCodes.plan,
            ownerText: '张工',
            dueRaw: '11.1',
            state: StageState.done,
          ),
          IssueStage(
            code: StageCodes.design,
            ownerText: '李工',
            dueRaw: '',
            dueDate: designDue,
            state: designState,
          ),
          IssueStage(
            code: StageCodes.material,
            ownerText: '王工',
            dueRaw: '',
            state: StageState.notStarted,
          ),
          IssueStage(
            code: StageCodes.assembly,
            ownerText: '赵工',
            dueRaw: '',
            dueDate: assemblyDue,
            state: StageState.inProgress,
          ),
        ],
      );
    }

    test('设计晚于装配即倒挂', () {
      final issue = issueWith(
        status: IssueStatuses.designing,
        designDue: DateTime(2026, 11, 20),
        assemblyDue: DateTime(2026, 11, 10),
      );
      expect(Rules.isPlanInverted(issue), isTrue);
    });

    test('逾期取未完成阶段的最长天数', () {
      final today = DateTime(2026, 11, 24);
      final issue = issueWith(
        status: IssueStatuses.designing,
        designDue: DateTime(2026, 11, 21),
        assemblyDue: DateTime(2026, 11, 17),
      );
      expect(Rules.overdueDays(issue, today: today), 7);
    });

    test('已关闭不计逾期', () {
      final today = DateTime(2026, 11, 24);
      final issue = issueWith(
        status: IssueStatuses.closed,
        designDue: DateTime(2026, 11, 1),
        assemblyDue: DateTime(2026, 11, 2),
      );
      expect(Rules.overdueDays(issue, today: today), 0);
    });
  });
}
