import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/generated/soitho_api.dart';
import '../../data/cached.dart';
import '../../data/finance_providers.dart';
import '../../data/write_api.dart';
import '../../shared/issues.dart';
import '../../shared/money.dart';
import '../../shared/widgets.dart';
import '../reconciliation/manual_entry_sheet.dart' show parseAmount;
import 'budget_page.dart' show fundKindLabel;

/// Changes the numbers of the plan: amounts, days, limits and debt balances.
/// Adding or removing funds, incomes and debts stays on the website, where
/// there is room for it. The server checks the plan and refuses to overwrite
/// a change the partner saved in the meantime.
class BudgetEditPage extends ConsumerStatefulWidget {
  const BudgetEditPage({super.key});

  @override
  ConsumerState<BudgetEditPage> createState() => _BudgetEditPageState();
}

/// One number of the plan, at a path inside the budget JSON.
class _NumberField {
  _NumberField(this.path, int? value, {this.optional = false, this.day = false})
    : controller = TextEditingController(
        text: value == null ? '' : (day ? '$value' : _group(value)),
      );

  final List<Object> path;
  final bool optional;
  final bool day;
  final TextEditingController controller;

  static String _group(int v) => formatVnd(v).replaceAll(' ₫', '');

  int? get value =>
      day ? int.tryParse(controller.text.trim()) : parseAmount(controller.text);
}

class _BudgetEditPageState extends ConsumerState<BudgetEditPage> {
  EditableBudget? _loaded;
  Object? _loadError;
  Map<String, Object?> _json = {};
  final List<_NumberField> _fields = [];
  bool _saving = false;
  List<String> _issues = const [];

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    for (final f in _fields) {
      f.controller.dispose();
    }
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loadError = null;
      _loaded = null;
    });
    try {
      final loaded = await ref.read(financeWritesProvider).loadBudget();
      if (!mounted) return;
      for (final f in _fields) {
        f.controller.dispose();
      }
      _fields.clear();
      // A deep, mutable copy to edit.
      _json = jsonDecode(
        jsonEncode(loaded.budget.toJson()),
      ) as Map<String, Object?>;
      setState(() {
        _loaded = loaded;
        _issues = const [];
      });
    } on Object catch (e) {
      if (mounted) setState(() => _loadError = e);
    }
  }

  Object? _at(List<Object> path) {
    Object? node = _json;
    for (final p in path) {
      node = p is int ? (node as List)[p] : (node as Map)[p];
    }
    return node;
  }

  void _put(List<Object> path, Object? value) {
    Object? node = _json;
    for (final p in path.take(path.length - 1)) {
      node = p is int ? (node as List)[p] : (node as Map)[p];
    }
    (node as Map)[path.last] = value;
  }

  _NumberField _field(
    List<Object> path, {
    bool optional = false,
    bool day = false,
  }) {
    final key = path.join('.');
    for (final f in _fields) {
      if (f.path.join('.') == key) return f;
    }
    final f = _NumberField(
      path,
      _at(path) as int?,
      optional: optional,
      day: day,
    );
    f.controller.addListener(() => setState(() {}));
    _fields.add(f);
    return f;
  }

  int _sum(Iterable<_NumberField> fields) =>
      fields.fold(0, (s, f) => s + (f.value ?? 0));

  Future<void> _save() async {
    final loaded = _loaded!;
    for (final f in _fields) {
      final v = f.value;
      if (v == null && !f.optional) {
        setState(() => _issues = const ['Điền đủ các ô số (có thể nhập 0).']);
        return;
      }
      _put(f.path, v);
    }
    setState(() {
      _saving = true;
      _issues = const [];
    });
    try {
      await ref
          .read(financeWritesProvider)
          .saveBudget(Budget.fromJson(_json), loaded.etag);
      ref.invalidate(budgetProvider);
      ref.invalidate(dashboardProvider);
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(const SnackBar(content: Text('Đã lưu ngân sách.')));
      Navigator.pop(context, true);
    } on BudgetChanged {
      if (!mounted) return;
      setState(() => _saving = false);
      await showDialog<void>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('Ngân sách vừa được sửa'),
          content: const Text(
            'Người kia vừa lưu ngân sách trong lúc anh chị đang sửa. App sẽ tải bản mới nhất; anh chị sửa lại phần của mình trên đó để không ghi đè thay đổi của nhau.',
          ),
          actions: [
            FilledButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('Tải bản mới'),
            ),
          ],
        ),
      );
      ref.invalidate(budgetProvider);
      await _load();
    } on DioException catch (e) {
      if (!mounted) return;
      final issues = problemIssues(e);
      setState(() {
        _saving = false;
        _issues = issues.isNotEmpty ? issues : [describeError(e)];
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Sửa ngân sách'),
        actions: [
          if (_loaded != null)
            TextButton(
              key: const Key('budget-save'),
              onPressed: _saving ? null : _save,
              child: _saving
                  ? const SizedBox(
                      width: 20,
                      height: 20,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Text('Lưu'),
            ),
        ],
      ),
      body: _loadError != null
          ? MessageView.error(_loadError!, onRetry: _load)
          : _loaded == null
          ? const Center(child: CircularProgressIndicator())
          : _form(context),
    );
  }

  Widget _form(BuildContext context) {
    final theme = Theme.of(context);
    final b = _loaded!.budget;
    final fundNames = {for (final f in b.funds) f.key: f.name};
    final quiet = theme.textTheme.bodySmall?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
    );
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 48),
      children: [
        Text(
          'Sửa số tiền, ngày và hạn mức. Thêm hoặc bớt quỹ, khoản thu, khoản nợ thì làm trên trang web.',
          style: quiet,
        ),
        if (_issues.isNotEmpty) ...[
          const SizedBox(height: 12),
          Card(
            key: const Key('budget-issues'),
            color: theme.colorScheme.errorContainer,
            child: Padding(
              padding: const EdgeInsets.all(12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  for (final i in _issues)
                    Text(
                      '• $i',
                      style: TextStyle(
                        color: theme.colorScheme.onErrorContainer,
                      ),
                    ),
                ],
              ),
            ),
          ),
        ],
        const SectionTitle('Chung'),
        _money(
          _field(['approvalThreshold']),
          'Chi ngoài kế hoạch từ mức này cần người kia đồng ý',
          key: 'approvalThreshold',
        ),
        const SectionTitle('Khoản thu'),
        for (var i = 0; i < b.incomes.length; i++) ...[
          Text(b.incomes[i].name, style: theme.textTheme.titleSmall),
          const SizedBox(height: 8),
          Row(
            children: [
              Expanded(
                flex: 3,
                child: _money(
                  _field(['incomes', i, 'amount']),
                  'Số tiền',
                  key: 'income-$i-amount',
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                flex: 2,
                child: _day(
                  _field(['incomes', i, 'payDay'], day: true),
                  'Ngày nhận',
                ),
              ),
            ],
          ),
          for (var a = 0; a < b.incomes[i].allocations.length; a++)
            Padding(
              padding: const EdgeInsets.only(left: 16),
              child: _money(
                _field(['incomes', i, 'allocations', a, 'amount']),
                'Vào quỹ ${fundNames[b.incomes[i].allocations[a].fundKey] ?? b.incomes[i].allocations[a].fundKey}',
                key: 'income-$i-alloc-$a',
              ),
            ),
          _balanceNote(
            context,
            have: _field(['incomes', i, 'amount']).value ?? 0,
            used: _sum([
              for (var a = 0; a < b.incomes[i].allocations.length; a++)
                _field(['incomes', i, 'allocations', a, 'amount']),
            ]),
            what: 'đã chia',
          ),
          const SizedBox(height: 16),
        ],
        const SectionTitle('Quỹ'),
        for (var f = 0; f < b.funds.length; f++) ...[
          Text(
            '${b.funds[f].name} · ${fundKindLabel(b.funds[f].kind)}',
            style: theme.textTheme.titleSmall,
          ),
          const SizedBox(height: 8),
          _money(
            _field(['funds', f, 'monthlyAmount']),
            'Mỗi tháng',
            key: 'fund-$f-monthly',
          ),
          if (const ['emergency', 'savings'].contains(b.funds[f].kind.toJson()))
            _money(
              _field(['funds', f, 'targetAmount'], optional: true),
              'Mục tiêu (để trống nếu không có)',
              key: 'fund-$f-target',
            ),
          for (var c = 0; c < b.funds[f].categories.length; c++)
            Padding(
              padding: const EdgeInsets.only(left: 16),
              child: _money(
                _field(['funds', f, 'categories', c, 'monthlyLimit']),
                'Hạn mức ${b.funds[f].categories[c].name}',
                key: 'fund-$f-cat-$c',
              ),
            ),
          if (b.funds[f].categories.isNotEmpty)
            _balanceNote(
              context,
              have: _field(['funds', f, 'monthlyAmount']).value ?? 0,
              used: _sum([
                for (var c = 0; c < b.funds[f].categories.length; c++)
                  _field(['funds', f, 'categories', c, 'monthlyLimit']),
              ]),
              what: 'các hạng mục cộng lại',
            ),
          const SizedBox(height: 16),
        ],
        if (b.debts.isNotEmpty) const SectionTitle('Khoản nợ'),
        for (var d = 0; d < b.debts.length; d++) ...[
          Text(b.debts[d].name, style: theme.textTheme.titleSmall),
          const SizedBox(height: 8),
          Row(
            children: [
              Expanded(
                flex: 3,
                child: _money(
                  _field(['debts', d, 'monthlyPayment']),
                  'Trả mỗi tháng',
                  key: 'debt-$d-monthly',
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                flex: 2,
                child: _day(
                  _field(['debts', d, 'dueDay'], day: true),
                  'Ngày trả',
                ),
              ),
            ],
          ),
          _money(
            _field(['debts', d, 'remainingBalance'], optional: true),
            'Còn nợ (để trống nếu chưa biết)',
            key: 'debt-$d-balance',
          ),
          const SizedBox(height: 16),
        ],
      ],
    );
  }

  Widget _balanceNote(
    BuildContext context, {
    required int have,
    required int used,
    required String what,
  }) {
    final theme = Theme.of(context);
    final ok = have == used;
    return Padding(
      padding: const EdgeInsets.only(left: 16, bottom: 4),
      child: Text(
        ok
            ? '✓ $what ${formatVnd(used)}'
            : '$what ${formatVnd(used)}, chênh ${formatVnd(have - used)}',
        style: theme.textTheme.bodySmall?.copyWith(
          color: ok ? theme.colorScheme.primary : theme.colorScheme.error,
        ),
      ),
    );
  }

  Widget _money(_NumberField f, String label, {required String key}) => Padding(
    padding: const EdgeInsets.only(bottom: 8),
    child: TextField(
      key: Key('budget-$key'),
      controller: f.controller,
      keyboardType: TextInputType.number,
      inputFormatters: [FilteringTextInputFormatter.digitsOnly, _Grouping()],
      decoration: InputDecoration(
        labelText: label,
        suffixText: '₫',
        border: const OutlineInputBorder(),
        isDense: true,
      ),
    ),
  );

  Widget _day(_NumberField f, String label) => Padding(
    padding: const EdgeInsets.only(bottom: 8),
    child: TextField(
      controller: f.controller,
      keyboardType: TextInputType.number,
      inputFormatters: [
        FilteringTextInputFormatter.digitsOnly,
        LengthLimitingTextInputFormatter(2),
      ],
      decoration: InputDecoration(
        labelText: label,
        border: const OutlineInputBorder(),
        isDense: true,
      ),
    ),
  );
}

class _Grouping extends TextInputFormatter {
  @override
  TextEditingValue formatEditUpdate(
    TextEditingValue old,
    TextEditingValue value,
  ) {
    if (value.text.isEmpty) return value;
    final text = formatVnd(int.parse(value.text)).replaceAll(' ₫', '');
    return TextEditingValue(
      text: text,
      selection: TextSelection.collapsed(offset: text.length),
    );
  }
}
