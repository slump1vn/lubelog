Map<String, Object?> tx(
  int id, {
  String dir = 'out',
  int amount = 50000,
  String at = '2026-11-21T03:15:00Z',
}) => {
  'id': id,
  'occurredAtUtc': at,
  'direction': dir,
  'amount': amount,
  'balance': 12000000 - id,
  'content': 'Giao dich $id',
  'reference': 'FT$id',
};

final dashboardJson = <String, Object?>{
  'today': '2026-11-21',
  'account': {
    'linked': true,
    'bankName': 'VietinBank',
    'maskedNumber': '•••• 6789',
    'balance': 12345000,
    'lastSyncUtc': '2026-11-21T03:00:00Z',
    'lastSyncSucceeded': true,
  },
  'cycle': {
    'start': '2026-11-05',
    'end': '2026-12-04',
    'day': 17,
    'days': 30,
    'moneyIn': 25000000,
    'moneyOut': 9800000,
    'unrecordedIn': 0,
    'unrecordedOut': 300000,
    'net': 15200000,
    'projectedOut': 17300000,
  },
  'balanceHistory': [
    {'date': '2026-11-19', 'balance': 13000000},
    {'date': '2026-11-20', 'balance': 12600000},
    {'date': '2026-11-21', 'balance': 12345000},
  ],
  'upcoming': [
    {
      'date': '2026-11-21',
      'name': 'Trả nợ',
      'amount': 8000000,
      'direction': 'out',
    },
  ],
  'recent': [tx(3), tx(2, dir: 'in', amount: 16000000)],
};

final budgetJson = <String, Object?>{
  'currency': 'VND',
  'approvalThreshold': 1000000,
  'cycleStartDay': 5,
  'incomes': [
    {
      'key': 'l1',
      'name': 'Lương Sói',
      'amount': 9000000,
      'payDay': 5,
      'allocations': [
        {'fundKey': 'debt', 'amount': 8000000},
      ],
    },
  ],
  'funds': [
    {
      'key': 'debt',
      'name': 'Quỹ trả nợ',
      'kind': 'debt',
      'monthlyAmount': 8000000,
      'targetAmount': null,
      'ownerUserId': null,
      'categories': <Object?>[],
    },
    {
      'key': 'sos',
      'name': 'Quỹ dự phòng',
      'kind': 'emergency',
      'monthlyAmount': 2000000,
      'targetAmount': 25000000,
      'ownerUserId': null,
      'categories': [
        {'key': 'x', 'name': 'Sửa xe', 'monthlyLimit': 500000},
      ],
    },
  ],
  'debts': [
    {
      'key': 'd',
      'name': 'Nợ ngân hàng',
      'monthlyPayment': 8000000,
      'dueDay': 21,
      'remainingBalance': null,
      'annualInterestRate': null,
      'fundKey': 'debt',
    },
  ],
  'updatedAtUtc': '2026-11-20T10:00:00Z',
};
